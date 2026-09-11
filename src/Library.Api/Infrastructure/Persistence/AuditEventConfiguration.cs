namespace Library.Api.Infrastructure.Persistence;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(auditEvent => auditEvent.Id);

        builder.Property(auditEvent => auditEvent.Id)
            .ValueGeneratedOnAdd();

        builder.HasIndex(auditEvent => new { auditEvent.EntityType, auditEvent.EntityId, auditEvent.OccurredAtUtc });

        builder.HasIndex(auditEvent => auditEvent.CorrelationId);

        builder.Property(auditEvent => auditEvent.EntityType)
            .HasColumnName("entity_type")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.EntityId)
            .HasColumnName("entity_id")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.Action)
            .HasColumnName("action")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.Actor)
            .HasColumnName("actor")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();
    }
}
