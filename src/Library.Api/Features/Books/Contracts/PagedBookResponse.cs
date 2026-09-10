namespace Library.Api.Features.Books.Contracts;

public sealed record PagedBookResponse(IReadOnlyList<BookResponse> Items, int Page, int PageSize, int TotalCount)
{
    public static PagedBookResponse From(PagedResult<Book> paged) => new(
        paged.Items.Select(BookResponse.From).ToList(),
        paged.Page,
        paged.PageSize,
        paged.TotalCount);
}
