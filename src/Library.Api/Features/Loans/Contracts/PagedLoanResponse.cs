namespace Library.Api.Features.Loans.Contracts;

public sealed record PagedLoanResponse(IReadOnlyList<LoanResponse> Items, int Page, int PageSize, int TotalCount)
{
    public static PagedLoanResponse From(PagedResult<Loan> paged) => new(
        [.. paged.Items.Select(LoanResponse.From)],
        paged.Page,
        paged.PageSize,
        paged.TotalCount);
}
