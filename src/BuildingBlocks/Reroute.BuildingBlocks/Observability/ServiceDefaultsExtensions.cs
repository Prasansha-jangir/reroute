using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Reroute.BuildingBlocks.Observability;

/// <summary>
/// "Service defaults" shared by every service: OpenTelemetry (traces, metrics, logs), health checks,
/// problem details, HTTP resilience and the X-Trace-Id response header.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(
        this WebApplicationBuilder builder, string serviceName, params string[] extraSources)
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName, serviceInstanceId: Environment.MachineName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(t => t
                .AddSource(RerouteTelemetry.Name)
                .AddSource(extraSources)
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !IsInfrastructureEndpoint(ctx.Request.Path))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(RerouteTelemetry.Name)
                .AddMeter(extraSources)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        // OTLP (→ OpenTelemetry Collector) is enabled purely by configuration, e.g. OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString());

        // Standard resilience pipeline for every HttpClient: rate limiter, total timeout, retry (jitter), circuit breaker, attempt timeout.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        builder.Services.AddSingleton(TimeProvider.System);
        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use((context, next) =>
        {
            if (Activity.Current is { } activity)
            {
                context.Response.Headers["X-Trace-Id"] = activity.TraceId.ToString();
            }

            return next(context);
        });
        return app;
    }

    public static WebApplication MapServiceDefaults(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint("/metrics");

        // Liveness checks only the process — never dependencies (avoids cascading restarts on a DB blip).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready");
        app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
        return app;
    }

    private static bool IsInfrastructureEndpoint(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics");
}
