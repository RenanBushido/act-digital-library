namespace Library.Api.Features.Books;

public static class GetBookAvailability
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AppDbContext dbContext,
        BookCache bookCache,
        CancellationToken cancellationToken)
    {
        var cached = await bookCache.GetAvailabilityAsync(id, cancellationToken);
        if (cached is not null)
        {
            return TypedResults.Ok(cached);
        }

        var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (book is null)
        {
            return BookErrors.NotFound(id).ToProblem();
        }

        var response = BookAvailabilityResponse.From(book);
        await bookCache.SetAvailabilityAsync(id, response, cancellationToken);

        return TypedResults.Ok(response);
    }
}
