namespace Library.Api.Features.Loans;

public sealed record CreateLoanRequest(Guid BookId, Guid UserId);

public static class CreateLoan
{
    public static async Task<IResult> HandleAsync(
        CreateLoanRequest request,
        HttpContext httpContext,
        [FromHeader(Name = "X-Actor")] string? actor,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<LoanOptions> loanOptions,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        if (request.BookId == Guid.Empty || request.UserId == Guid.Empty)
        {
            return LoanErrors.ValidationFailed("BookId and UserId are required.").ToProblem();
        }

        var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == request.BookId, cancellationToken);
        if (book is null)
        {
            return LoanErrors.BookNotFound(request.BookId).ToProblem();
        }

        var userExists = await dbContext.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            return LoanErrors.UserNotFound(request.UserId).ToProblem();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var affected = await dbContext.Books
            .Where(b => b.Id == request.BookId && b.IsActive && b.AvailableCopies > 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.AvailableCopies, b => b.AvailableCopies - 1), cancellationToken);

        if (affected == 0)
        {
            return book.IsActive
                ? LoanErrors.NoCopyAvailable(request.BookId).ToProblem()
                : LoanErrors.BookInactive(request.BookId).ToProblem();
        }

        var loan = Loan.Create(request.BookId, request.UserId, loanOptions.Value.DueDays, timeProvider);

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor;
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;

        using var payload = JsonSerializer.SerializeToDocument(new
        {
            bookId = loan.BookId,
            userId = loan.UserId,
            status = new { after = loan.Status.ToString() },
            dueAtUtc = loan.DueAtUtc,
        });
        var auditEvent = AuditEvent.Create(
            "Loan",
            loan.Id,
            "LoanCreated",
            resolvedActor,
            timeProvider.GetUtcNow(),
            correlationId,
            payload);

        dbContext.Loans.Add(loan);
        dbContext.AuditEvents.Add(auditEvent);

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = LoanResponse.From(loan);
        using var responseBody = JsonSerializer.SerializeToDocument(response, jsonOptions.Value.SerializerOptions);

        await dbContext.IdempotencyKeys
            .Where(k => k.Key == idempotencyKey && k.Endpoint == RequireIdempotencyKeyFilter.Endpoint)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(k => k.State, IdempotencyState.Completed)
                    .SetProperty(k => k.StatusCode, StatusCodes.Status201Created)
                    .SetProperty(k => k.ResponseBody, responseBody)
                    .SetProperty(k => k.ResourceId, loan.Id),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Created($"/loans/{loan.Id}", response);
    }
}
