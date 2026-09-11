namespace Library.Api.Features.Audit.Contracts;

public sealed record AuditEventResponse(
    string Id,
    string EntityType,
    Guid EntityId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    JsonElement Payload)
{
    public static AuditEventResponse From(AuditEvent auditEvent) => new(
        auditEvent.Id.ToString(),
        auditEvent.EntityType,
        auditEvent.EntityId,
        auditEvent.Action,
        auditEvent.Actor,
        auditEvent.OccurredAtUtc,
        auditEvent.CorrelationId,
        auditEvent.Payload.RootElement);
}
