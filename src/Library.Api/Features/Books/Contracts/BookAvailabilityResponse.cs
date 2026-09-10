namespace Library.Api.Features.Books.Contracts;

public sealed record BookAvailabilityResponse(int TotalCopies, int AvailableCopies, bool IsActive)
{
    public static BookAvailabilityResponse From(Book book) =>
        new(book.TotalCopies, book.AvailableCopies, book.IsActive);
}
