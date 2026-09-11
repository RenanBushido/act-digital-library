namespace Library.Api.Domain.Idempotency;

public sealed class IdempotencyKey
{
    public string Key { get; private set; }
    public string Endpoint { get; private set; }
    public string RequestHash { get; private set; }
    public IdempotencyState State { get; private set; }
    public int? StatusCode { get; private set; }
    public JsonDocument? ResponseBody { get; private set; }
    public Guid? ResourceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    private IdempotencyKey(
        string key,
        string endpoint,
        string requestHash,
        IdempotencyState state,
        int? statusCode,
        JsonDocument? responseBody,
        Guid? resourceId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Key = key;
        Endpoint = endpoint;
        RequestHash = requestHash;
        State = state;
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ResourceId = resourceId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public static IdempotencyKey Reserve(string key, string endpoint, string requestHash, DateTimeOffset createdAtUtc, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DomainException("Idempotency key cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new DomainException("Idempotency endpoint cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new DomainException("Idempotency request hash cannot be empty.");
        }

        return new IdempotencyKey(
            key,
            endpoint,
            requestHash,
            IdempotencyState.InFlight,
            statusCode: null,
            responseBody: null,
            resourceId: null,
            createdAtUtc,
            createdAtUtc.Add(ttl));
    }
}
