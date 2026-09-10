namespace Library.Api.Features.Books;

public static class DeactivateBook
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IDistributedCache cache,
        ILogger<BooksLog> logger,
        CancellationToken cancellationToken)
    {
        var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (book is null)
        {
            return BookErrors.NotFound(id).ToProblem();
        }

        var wasActive = book.IsActive;

        book.Deactivate(timeProvider);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasActive)
        {
            await CacheReadThrough.RemoveAsync(cache, logger, GetBookAvailability.CacheKey(id), cancellationToken);
            await CacheReadThrough.RemoveAsync(cache, logger, ListBooks.CacheKey(ListBooks.DefaultPage, ListBooks.DefaultPageSize), cancellationToken);
        }

        return TypedResults.NoContent();
    }
}
