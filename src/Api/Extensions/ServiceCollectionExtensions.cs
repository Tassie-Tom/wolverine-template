using Api.Auth;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Postgresql;

namespace Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddSerilogLogging(this WebApplicationBuilder builder)
    {
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
    }

    public static void AddObservability(this WebApplicationBuilder builder)
    {
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
    }

    public static async Task AddSupabaseAuthAsync(this WebApplicationBuilder builder)
    {
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
                            .GetRequiredService<ILogger<SupabaseJwksService>>();

                        logger.LogError(context.Exception,
                            "JWT Authentication failed: {Error}",
                            context.Exception.Message);

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<SupabaseJwksService>>();

                        var sub = context.Principal?.FindFirst("sub")?.Value;
                        logger.LogDebug("JWT validated for user {Sub}", sub);

                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<SupabaseJwksService>>();

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
    }

    public static void AddWolverineMessaging(this WebApplicationBuilder builder)
    {
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
    }
}
