namespace Library.Api.Features.Books.Contracts;

public sealed record BookResponse(
    Guid Id,
    string Title,
    string Isbn,
    string Author,
    int TotalCopies,
    int AvailableCopies,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static BookResponse From(Book book) => new(
        book.Id,
        book.Title,
        book.Isbn.Value,
        book.Author,
        book.TotalCopies,
        book.AvailableCopies,
        book.IsActive,
        book.CreatedAtUtc,
        book.UpdatedAtUtc);
}
