namespace Library.Api.Domain.Loan;

public sealed class Loan
{
    public Guid Id { get; private set; }
    public Guid BookId { get; private set; }
    public Guid UserId { get; private set; }
    public LoanStatus Status { get; private set; }
    public DateTimeOffset LoanedAtUtc { get; private set; }
    public DateTimeOffset DueAtUtc { get; private set; }
    public DateTimeOffset? ReturnedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    private Loan(
        Guid id,
        Guid bookId,
        Guid userId,
        LoanStatus status,
        DateTimeOffset loanedAtUtc,
        DateTimeOffset dueAtUtc,
        DateTimeOffset? returnedAtUtc,
        DateTimeOffset? cancelledAtUtc)
    {
        Id = id;
        BookId = bookId;
        UserId = userId;
        Status = status;
        LoanedAtUtc = loanedAtUtc;
        DueAtUtc = dueAtUtc;
        ReturnedAtUtc = returnedAtUtc;
        CancelledAtUtc = cancelledAtUtc;
    }

    public static Loan Create(Guid bookId, Guid userId, int dueDays, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        return new Loan(
            Guid.NewGuid(),
            bookId,
            userId,
            LoanStatus.Active,
            now,
            now.AddDays(dueDays),
            returnedAtUtc: null,
            cancelledAtUtc: null);
    }

    public void Return(TimeProvider timeProvider)
    {
        if (Status != LoanStatus.Active)
        {
            throw new DomainException($"Loan '{Id}' is not active and cannot be returned.");
        }

        Status = LoanStatus.Returned;
        ReturnedAtUtc = timeProvider.GetUtcNow();
    }

    public void Cancel(TimeProvider timeProvider)
    {
        if (Status != LoanStatus.Active)
        {
            throw new DomainException($"Loan '{Id}' is not active and cannot be cancelled.");
        }

        Status = LoanStatus.Cancelled;
        CancelledAtUtc = timeProvider.GetUtcNow();
    }
}
