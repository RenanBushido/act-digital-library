namespace Library.Api.Features.Books;

public static class GetBook
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        return book is null
            ? BookErrors.NotFound(id).ToProblem()
            : TypedResults.Ok(BookResponse.From(book));
    }
}
