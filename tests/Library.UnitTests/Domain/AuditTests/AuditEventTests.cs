namespace Library.UnitTests.Domain.AuditTests;

public class AuditEventTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_with_valid_data_succeeds()
    {
        var entityId = Guid.NewGuid();
        using var payload = JsonDocument.Parse("""{"status":{"before":"Active","after":"Returned"}}""");

        var auditEvent = AuditEvent.Create(
            "Loan",
            entityId,
            "LoanReturned",
            "anonymous",
            Clock.GetUtcNow(),
            "correlation-id",
            payload);

        Assert.Equal("Loan", auditEvent.EntityType);
        Assert.Equal(entityId, auditEvent.EntityId);
        Assert.Equal("LoanReturned", auditEvent.Action);
        Assert.Equal("anonymous", auditEvent.Actor);
        Assert.Equal("correlation-id", auditEvent.CorrelationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_entity_type_is_empty(string entityType)
    {
        using var payload = JsonDocument.Parse("{}");

        Assert.Throws<DomainException>(() =>
            AuditEvent.Create(entityType, Guid.NewGuid(), "LoanCreated", "anonymous", Clock.GetUtcNow(), "correlation-id", payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_actor_is_empty(string actor)
    {
        using var payload = JsonDocument.Parse("{}");

        Assert.Throws<DomainException>(() =>
            AuditEvent.Create("Loan", Guid.NewGuid(), "LoanCreated", actor, Clock.GetUtcNow(), "correlation-id", payload));
    }
}
