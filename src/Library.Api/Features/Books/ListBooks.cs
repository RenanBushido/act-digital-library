namespace Library.Api.Features.Books;

public static class ListBooks
{
    public static async Task<IResult> HandleAsync(
        int? page,
        int? pageSize,
        string? title,
        string? author,
        string? isbn,
        bool? includeInactive,
        AppDbContext dbContext,
        BookCache bookCache,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page is > 0 ? page.Value : PaginationDefaults.DefaultPage;
        var resolvedPageSize = pageSize is > 0 and <= PaginationDefaults.MaxPageSize ? pageSize.Value : PaginationDefaults.DefaultPageSize;
        var resolvedIncludeInactive = includeInactive ?? false;

        Isbn? normalizedIsbn = null;
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            try
            {
                normalizedIsbn = Isbn.Create(isbn);
            }
            catch (DomainException ex)
            {
                return BookErrors.ValidationFailed(ex.Message).ToProblem();
            }
        }

        var filter = new BookListFilter(title, author, isbn, resolvedIncludeInactive, resolvedPage, resolvedPageSize);

        var cached = await bookCache.GetListAsync(filter, cancellationToken);
        if (cached is not null)
        {
            return TypedResults.Ok(cached);
        }

        var query = dbContext.Books.AsQueryable();

        if (!resolvedIncludeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            query = query.Where(b => EF.Functions.ILike(b.Title, $"%{title}%"));
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            query = query.Where(b => EF.Functions.ILike(b.Author, $"%{author}%"));
        }

        if (normalizedIsbn is { } isbnValue)
        {
            query = query.Where(b => b.Isbn == isbnValue);
        }

        query = query.OrderBy(b => b.Title);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);

        var response = PagedBookResponse.From(new PagedResult<Book>(items, resolvedPage, resolvedPageSize, totalCount));

        await bookCache.SetListAsync(filter, response, cancellationToken);

        return TypedResults.Ok(response);
    }
}
