namespace Library.Api.Features.Users;

public static class GetUserLoans
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        int? page,
        int? pageSize,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AnyAsync(u => u.Id == id, cancellationToken);
        if (!userExists)
        {
            return LoanErrors.UserNotFound(id).ToProblem();
        }

        var resolvedPage = page is > 0 ? page.Value : PaginationDefaults.DefaultPage;
        var resolvedPageSize = pageSize is > 0 and <= PaginationDefaults.MaxPageSize ? pageSize.Value : PaginationDefaults.DefaultPageSize;

        var query = dbContext.Loans.Where(l => l.UserId == id).OrderByDescending(l => l.LoanedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);

        var response = PagedLoanResponse.From(new PagedResult<Loan>(items, resolvedPage, resolvedPageSize, totalCount));

        return TypedResults.Ok(response);
    }
}
