namespace Library.Api.Features.Loans;

public static class CancelLoan
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext httpContext,
        [FromHeader(Name = "X-Actor")] string? actor,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var loan = await dbContext.Loans.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (loan is null)
        {
            return LoanErrors.NotFound(id).ToProblem();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var cancelledAtUtc = timeProvider.GetUtcNow();

        var affected = await dbContext.Loans
            .Where(l => l.Id == id && l.Status == LoanStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.Status, LoanStatus.Cancelled)
                    .SetProperty(l => l.CancelledAtUtc, cancelledAtUtc),
                cancellationToken);

        if (affected == 0)
        {
            return LoanErrors.NotActive(id).ToProblem();
        }

        await dbContext.Books
            .Where(b => b.Id == loan.BookId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.AvailableCopies, b => b.AvailableCopies + 1), cancellationToken);

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor;
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;

        using var payload = JsonSerializer.SerializeToDocument(new
        {
            status = new { before = LoanStatus.Active.ToString(), after = LoanStatus.Cancelled.ToString() },
        });

        var auditEvent = AuditEvent.Create("Loan", loan.Id, "LoanCancelled", resolvedActor, cancelledAtUtc, correlationId, payload);
        dbContext.AuditEvents.Add(auditEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = new LoanResponse(
            loan.Id,
            loan.BookId,
            loan.UserId,
            LoanStatus.Cancelled.ToString(),
            loan.LoanedAtUtc,
            loan.DueAtUtc,
            loan.ReturnedAtUtc,
            cancelledAtUtc);

        return TypedResults.Ok(response);
    }
}
