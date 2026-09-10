namespace Library.Api.Features.Books;

public static class GetBookHistory
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        int? page,
        int? pageSize,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var bookExists = await dbContext.Books.AnyAsync(b => b.Id == id, cancellationToken);
        if (!bookExists)
        {
            return LoanErrors.BookNotFound(id).ToProblem();
        }

        var resolvedPage = page is > 0 ? page.Value : PaginationDefaults.DefaultPage;
        var resolvedPageSize = pageSize is > 0 and <= PaginationDefaults.MaxPageSize ? pageSize.Value : PaginationDefaults.DefaultPageSize;

        var query = dbContext.Loans.Where(l => l.BookId == id).OrderByDescending(l => l.LoanedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);

        var response = PagedLoanResponse.From(new PagedResult<Loan>(items, resolvedPage, resolvedPageSize, totalCount));

        return TypedResults.Ok(response);
    }
}
