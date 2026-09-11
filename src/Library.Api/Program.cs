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

app.Run();

public partial class Program;
