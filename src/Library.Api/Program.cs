if (args.Contains("--migrate-only"))
{
    return await Program.RunMigrateOnlyAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApiDatabase(builder.Configuration);
builder.Services.AddApiCaching(builder.Configuration);
builder.Services.AddApiProblemDetails();
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddValidation();
builder.Services.Configure<LoanOptions>(builder.Configuration.GetSection(LoanOptions.SectionName));

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseMiddleware<CorrelationIdMiddleware>();

// O argument binding de POST /loans lê o corpo antes do IEndpointFilter de idempotência rodar;
// sem bufferizar aqui (antes da leitura), o filtro encontraria o stream já consumido.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/loans")
    {
        context.Request.EnableBuffering();
    }

    await next(context);
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = ObservabilityExtensions.WriteHealthCheckResponseAsync,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
    ResponseWriter = ObservabilityExtensions.WriteHealthCheckResponseAsync,
});

app.MapBooksEndpoints();
app.MapLoansEndpoints();
app.MapUsersEndpoints();
app.MapAuditEventsEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    await migrationScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
}

app.Run();

return 0;

public partial class Program
{
    // Host reduzido, só com o suficiente para resolver `AppDbContext` - usado pelo modo
    // `--migrate-only` e pelos testes de integração que exercitam esse modo diretamente.
    internal static async Task<int> RunMigrateOnlyAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddJsonConsole();
        builder.Services.AddApiDatabase(builder.Configuration);

        await using var app = builder.Build();

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
            return 0;
        }
        catch (Exception ex)
        {
            app.Services.GetRequiredService<ILogger<Program>>().LogCritical(ex, "Migration failed");
            return 1;
        }
    }
}
