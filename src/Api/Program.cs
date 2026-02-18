using Api.Data;
using Api.Extensions;
using Api.Middleware;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;

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

// Infrastructure
builder.AddSerilogLogging();
builder.AddObservability();

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

// Authentication & Authorization
await builder.AddSupabaseAuthAsync();

// Email service (NullEmailService in dev — swap for real implementation in production)
builder.Services.AddScoped<IEmailService, NullEmailService>();

// EF Core + PostgreSQL
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("appdb");
    options.UseNpgsql(connectionString);
});

// Wolverine + messaging
builder.AddWolverineMessaging();

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
