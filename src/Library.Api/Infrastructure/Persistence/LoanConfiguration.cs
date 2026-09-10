namespace Library.Api.Infrastructure.Persistence;

public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("loans");

        builder.HasKey(loan => loan.Id);

        builder.Property(loan => loan.BookId)
            .HasColumnName("book_id")
            .IsRequired();

        builder.Property(loan => loan.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(loan => loan.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(loan => loan.LoanedAtUtc)
            .HasColumnName("loaned_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(loan => loan.DueAtUtc)
            .HasColumnName("due_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(loan => loan.ReturnedAtUtc)
            .HasColumnName("returned_at_utc")
            .HasColumnType("timestamptz");

        builder.Property(loan => loan.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc")
            .HasColumnType("timestamptz");

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(loan => loan.BookId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(loan => loan.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(loan => loan.BookId);
        builder.HasIndex(loan => loan.UserId);
    }
}
