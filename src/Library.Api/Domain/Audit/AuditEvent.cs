namespace Library.Api.Domain.Audit;

public sealed class AuditEvent
{
    public Guid Id { get; private set; }
    public string EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public string Action { get; private set; }
    public string Actor { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; }
    public JsonDocument Payload { get; private set; }

    private AuditEvent(
        Guid id,
        string entityType,
        Guid entityId,
        string action,
        string actor,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        JsonDocument payload)
    {
        Id = id;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Actor = actor;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        Payload = payload;
    }

    public static AuditEvent Create(
        string entityType,
        Guid entityId,
        string action,
        string actor,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        JsonDocument payload)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new DomainException("Entity type cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new DomainException("Action cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new DomainException("Actor cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new DomainException("Correlation id cannot be empty.");
        }

        return new AuditEvent(Guid.NewGuid(), entityType, entityId, action, actor, occurredAtUtc, correlationId, payload);
    }
}
