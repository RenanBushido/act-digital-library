namespace Library.Api.Features.Books;

public sealed class Book
{
    public Guid Id { get; private set; }
    public string Title { get; private set; }
    public Isbn Isbn { get; private set; }
    public string Author { get; private set; }
    public int TotalCopies { get; private set; }
    public int AvailableCopies { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Book(
        Guid id,
        string title,
        Isbn isbn,
        string author,
        int totalCopies,
        int availableCopies,
        bool isActive,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        Title = title;
        Isbn = isbn;
        Author = author;
        TotalCopies = totalCopies;
        AvailableCopies = availableCopies;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public static Book Create(
        string title,
        string isbn,
        string author,
        int totalCopies,
        int availableCopies,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Título não pode ser vazio.");
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            throw new DomainException("Autor não pode ser vazio.");
        }

        if (totalCopies <= 0)
        {
            throw new DomainException("Total de exemplares deve ser maior que zero.");
        }

        if (availableCopies < 0 || availableCopies > totalCopies)
        {
            throw new DomainException("Exemplares disponíveis deve estar entre 0 e o total de exemplares.");
        }

        var normalizedIsbn = Isbn.Create(isbn);
        var now = timeProvider.GetUtcNow();

        return new Book(
            Guid.NewGuid(),
            title.Trim(),
            normalizedIsbn,
            author.Trim(),
            totalCopies,
            availableCopies,
            isActive: true,
            now,
            now);
    }
}
