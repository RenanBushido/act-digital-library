namespace Library.Api.Features.Books;

public static class GetBookAvailability
{
    public static string CacheKey(Guid id) => $"books:{id}:availability";

    public static async Task<IResult> HandleAsync(
        Guid id,
        AppDbContext dbContext,
        IDistributedCache cache,
        ILogger<BooksLog> logger,
        CancellationToken cancellationToken)
    {
        var response = await CacheReadThrough.GetOrCreateAsync(
            cache,
            logger,
            CacheKey(id),
            async ct =>
            {
                var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == id, ct);
                return book is null ? null : BookAvailabilityResponse.From(book);
            },
            cancellationToken);

        return response is null
            ? BookErrors.NotFound(id).ToProblem()
            : TypedResults.Ok(response);
    }
}
