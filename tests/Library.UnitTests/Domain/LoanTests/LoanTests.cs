namespace Library.UnitTests.Domain.LoanTests;

public class LoanTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_with_valid_data_is_active_and_due_date_is_loaned_date_plus_due_days()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var loan = Loan.Create(bookId, userId, dueDays: 14, Clock);

        Assert.Equal(bookId, loan.BookId);
        Assert.Equal(userId, loan.UserId);
        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(loan.LoanedAtUtc.AddDays(14), loan.DueAtUtc);
        Assert.Null(loan.ReturnedAtUtc);
        Assert.Null(loan.CancelledAtUtc);
    }

    [Fact]
    public void Return_from_active_succeeds()
    {
        var loan = Loan.Create(Guid.NewGuid(), Guid.NewGuid(), dueDays: 14, Clock);

        loan.Return(Clock);

        Assert.Equal(LoanStatus.Returned, loan.Status);
        Assert.NotNull(loan.ReturnedAtUtc);
    }

    [Fact]
    public void Cancel_from_active_succeeds()
    {
        var loan = Loan.Create(Guid.NewGuid(), Guid.NewGuid(), dueDays: 14, Clock);

        loan.Cancel(Clock);

        Assert.Equal(LoanStatus.Cancelled, loan.Status);
        Assert.NotNull(loan.CancelledAtUtc);
    }

    [Fact]
    public void Return_from_returned_throws()
    {
        var loan = Loan.Create(Guid.NewGuid(), Guid.NewGuid(), dueDays: 14, Clock);
        loan.Return(Clock);

        Assert.Throws<DomainException>(() => loan.Return(Clock));
    }

    [Fact]
    public void Return_from_cancelled_throws()
    {
        var loan = Loan.Create(Guid.NewGuid(), Guid.NewGuid(), dueDays: 14, Clock);
        loan.Cancel(Clock);

        Assert.Throws<DomainException>(() => loan.Return(Clock));
    }

    [Fact]
    public void Cancel_from_returned_throws()
    {
        var loan = Loan.Create(Guid.NewGuid(), Guid.NewGuid(), dueDays: 14, Clock);
        loan.Return(Clock);

        Assert.Throws<DomainException>(() => loan.Cancel(Clock));
    }

    [Fact]
    public void Cancel_from_cancelled_throws()
    {
        var loan = Loan.Create(Guid.NewGuid(), Guid.NewGuid(), dueDays: 14, Clock);
        loan.Cancel(Clock);

        Assert.Throws<DomainException>(() => loan.Cancel(Clock));
    }
}
