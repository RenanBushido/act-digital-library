namespace Library.Api.Infrastructure.Persistence;

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(k => new { k.Key, k.Endpoint });

        builder.Property(k => k.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.Property(k => k.Endpoint)
            .HasColumnName("endpoint")
            .IsRequired();

        builder.Property(k => k.RequestHash)
            .HasColumnName("request_hash")
            .IsRequired();

        builder.Property(k => k.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(k => k.StatusCode)
            .HasColumnName("status_code");

        builder.Property(k => k.ResponseBody)
            .HasColumnName("response_body")
            .HasColumnType("jsonb");

        builder.Property(k => k.ResourceId)
            .HasColumnName("resource_id");

        builder.Property(k => k.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(k => k.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();
    }
}
