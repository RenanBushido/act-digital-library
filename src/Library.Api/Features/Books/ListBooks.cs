namespace Library.Api.Features.Books;

public static class ListBooks
{
    public static string CacheKey(int page, int pageSize) => $"books:list:page={page}:size={pageSize}";

    public static async Task<IResult> HandleAsync(
        int? page,
        int? pageSize,
        AppDbContext dbContext,
        IDistributedCache cache,
        ILogger<BooksLog> logger,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page is > 0 ? page.Value : PaginationDefaults.DefaultPage;
        var resolvedPageSize = pageSize is > 0 and <= PaginationDefaults.MaxPageSize ? pageSize.Value : PaginationDefaults.DefaultPageSize;

        var response = await CacheReadThrough.GetOrCreateAsync(
            cache,
            logger,
            CacheKey(resolvedPage, resolvedPageSize),
            async ct =>
            {
                var query = dbContext.Books.Where(b => b.IsActive).OrderBy(b => b.Title);

                var totalCount = await query.CountAsync(ct);
                var items = await query
                    .Skip((resolvedPage - 1) * resolvedPageSize)
                    .Take(resolvedPageSize)
                    .ToListAsync(ct);

                return PagedBookResponse.From(new PagedResult<Book>(items, resolvedPage, resolvedPageSize, totalCount));
            },
            cancellationToken);

        return TypedResults.Ok(response);
    }
}
