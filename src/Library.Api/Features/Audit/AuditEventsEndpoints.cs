namespace Library.Api.Features.Audit;

public static class AuditEventsEndpoints
{
    public static IEndpointRouteBuilder MapAuditEventsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/audit-events");

        group.MapGet("/", GetAuditEvents.HandleAsync);

        return app;
    }
}
