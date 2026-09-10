namespace Library.Api.Features.Books;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("books", table =>
            table.HasCheckConstraint(
                "ck_books_available_copies_bounds",
                "available_copies >= 0 AND available_copies <= total_copies"));

        builder.HasKey(book => book.Id);

        builder.Property(book => book.Title)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(book => book.Isbn)
            .HasColumnName("isbn")
            .HasConversion(isbn => isbn.Value, value => Isbn.Create(value))
            .IsRequired();

        builder.HasIndex(book => book.Isbn)
            .IsUnique();

        builder.Property(book => book.Author)
            .HasColumnName("author")
            .IsRequired();

        builder.Property(book => book.TotalCopies)
            .HasColumnName("total_copies")
            .IsRequired();

        builder.Property(book => book.AvailableCopies)
            .HasColumnName("available_copies")
            .IsRequired();

        builder.Property(book => book.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(book => book.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(book => book.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();
    }
}
