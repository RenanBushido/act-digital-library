namespace Library.Api.Features.Loans.Contracts;

public sealed record LoanResponse(
    Guid Id,
    Guid BookId,
    Guid UserId,
    string Status,
    DateTimeOffset LoanedAtUtc,
    DateTimeOffset DueAtUtc,
    DateTimeOffset? ReturnedAtUtc,
    DateTimeOffset? CancelledAtUtc)
{
    public static LoanResponse From(Loan loan) => new(
        loan.Id,
        loan.BookId,
        loan.UserId,
        loan.Status.ToString(),
        loan.LoanedAtUtc,
        loan.DueAtUtc,
        loan.ReturnedAtUtc,
        loan.CancelledAtUtc);
}
