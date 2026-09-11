namespace Library.Api.Domain.Idempotency;

public enum IdempotencyState
{
    InFlight,
    Completed,
}
