using Api.Auth;
using Api.Data;
using Api.Middleware;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Postgresql;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// OpenAPI + Scalar
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();
builder.Services.AddHttpClient();

// Serilog
builder.Services.AddSerilog((services, lc) =>
{
    lc.ReadFrom.Configuration(builder.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .Enrich.WithMachineName()
      .Enrich.WithEnvironmentName()
      .Enrich.WithProperty("Application", "Wolverine.Template")
      .WriteTo.Console();

    var seqUrl = builder.Configuration.GetConnectionString("seq");
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        lc.WriteTo.Seq(seqUrl);
    }
});

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Wolverine.Template"))
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation();
    })
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation();
    });

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics.AddOtlpExporter())
        .WithTracing(tracing => tracing.AddOtlpExporter());
}

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("appdb")
        ?? "Host=localhost;Database=appdb;Username=postgres;Password=postgres");

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                  "http://localhost:4200",
                  "https://localhost:4200",
                  "http://localhost:3000",
                  "https://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Supabase JWT Authentication
var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url is required");

var jwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";
using var startupHttpClient = new HttpClient();
var jwksJson = await startupHttpClient.GetStringAsync(jwksUrl);
var jwks = new JsonWebKeySet(jwksJson);
var initialKeys = jwks.GetSigningKeys().ToList();

Log.Information("[Startup] Loaded {KeyCount} JWKS signing keys from Supabase", initialKeys.Count);

builder.Services.AddSingleton<SupabaseJwksService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = initialKeys,
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = async context =>
            {
                var jwksService = context.HttpContext.RequestServices.GetRequiredService<SupabaseJwksService>();
                var keys = await jwksService.GetSigningKeysAsync();
                context.Options.TokenValidationParameters.IssuerSigningKeys = keys;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                logger.LogError(context.Exception,
                    "JWT Authentication failed: {Error}",
                    context.Exception.Message);

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                var sub = context.Principal?.FindFirst("sub")?.Value;
                logger.LogDebug("JWT validated for user {Sub}", sub);

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                logger.LogWarning("JWT authentication challenge: {Error} - {ErrorDescription}",
                    context.Error,
                    context.ErrorDescription);

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddTransient<IClaimsTransformation, SupabaseClaimsTransformation>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
});

// Email service (NullEmailService in dev — swap for real implementation in production)
builder.Services.AddScoped<IEmailService, NullEmailService>();

// EF Core + PostgreSQL
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("appdb");
    options.UseNpgsql(connectionString);
});

// Wolverine
builder.Host.UseWolverine(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("appdb");

    opts.PersistMessagesWithPostgresql(connectionString!);
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();

    opts.Policies.OnException<Npgsql.NpgsqlException>()
        .RetryWithCooldown(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(1));

    opts.Policies.OnException<HttpRequestException>()
        .RetryWithCooldown(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2));

    opts.UseFluentValidation();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();

// OpenAPI + Scalar (dev only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Auto-migrate database on startup
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var userId = httpContext.User.FindFirst("sub")?.Value;
        if (userId != null)
            diagnosticContext.Set("UserId", userId);
    };
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseResponseCaching();

// Auto-sync users from JWT to database
app.UseMiddleware<AutoUserSyncMiddleware>();

// Health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
