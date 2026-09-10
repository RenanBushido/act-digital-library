namespace Library.Api.Features.Books;

public sealed record UpdateBookRequest(
    [property: Required, MinLength(1)] string Title,
    [property: Required, MinLength(1)] string Author,
    [property: Range(1, int.MaxValue)] int TotalCopies);

public static class UpdateBook
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateBookRequest request,
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

        if (!book.IsActive)
        {
            return BookErrors.Inactive(id).ToProblem();
        }

        var delta = request.TotalCopies - book.TotalCopies;
        var newAvailableCopies = book.AvailableCopies + delta;

        if (newAvailableCopies < 0)
        {
            return BookErrors.InsufficientAvailableCopies(id).ToProblem();
        }

        book.UpdateDetails(request.Title, request.Author, request.TotalCopies, newAvailableCopies, timeProvider);

        await dbContext.SaveChangesAsync(cancellationToken);

        await CacheReadThrough.RemoveAsync(cache, logger, GetBookAvailability.CacheKey(id), cancellationToken);
        await CacheReadThrough.RemoveAsync(cache, logger, ListBooks.CacheKey(ListBooks.DefaultPage, ListBooks.DefaultPageSize), cancellationToken);

        return TypedResults.Ok(BookResponse.From(book));
    }
}
