namespace Library.Api.Extensions;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddApiObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<LoanMetrics>();

        services.AddHealthChecks()
            .AddNpgSql(
                sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? throw new InvalidOperationException("Connection string 'Postgres' not found."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"])
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"]);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Library.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = httpContext =>
                        !httpContext.Request.Path.StartsWithSegments("/health/live") &&
                        !httpContext.Request.Path.StartsWithSegments("/health/ready");
                })
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                // Sem argumento: resolve o `IConnectionMultiplexer` do DI (o mesmo singleton
                // registrado em `AddApiCaching`) quando o TracerProvider é montado, em vez de
                // exigir a instância já construída no momento deste registro.
                .AddRedisInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(LoanMetrics.MeterName)
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        return services;
    }

    public static Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        });

        return context.Response.WriteAsync(payload);
    }
}
