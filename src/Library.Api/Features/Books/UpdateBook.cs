using System.Text.Json.Nodes;

namespace Library.Api.Features.Books;

public sealed record UpdateBookRequest(
    [property: Required, MinLength(1)] string Title,
    [property: Required, MinLength(1)] string Author,
    [property: Range(1, int.MaxValue)] int TotalCopies);

public static class UpdateBook
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UpdateBookRequest request,
        HttpContext httpContext,
        [FromHeader(Name = "X-Actor")] string? actor,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IDistributedCache cache,
        ILogger<BooksLog> logger,
        CancellationToken cancellationToken)
    {
        var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (book is null)
        {
            return BookErrors.NotFound(id).ToProblem();
        }

        if (!book.IsActive)
        {
            return BookErrors.Inactive(id).ToProblem();
        }

        var delta = request.TotalCopies - book.TotalCopies;
        var newAvailableCopies = book.AvailableCopies + delta;

        if (newAvailableCopies < 0)
        {
            return BookErrors.InsufficientAvailableCopies(id).ToProblem();
        }

        var previousTitle = book.Title;
        var previousAuthor = book.Author;
        var previousTotalCopies = book.TotalCopies;
        var previousAvailableCopies = book.AvailableCopies;

        book.UpdateDetails(request.Title, request.Author, request.TotalCopies, newAvailableCopies, timeProvider);

        var before = new JsonObject();
        var after = new JsonObject();

        if (!string.Equals(previousTitle, request.Title, StringComparison.Ordinal))
        {
            before["title"] = previousTitle;
            after["title"] = request.Title;
        }

        if (!string.Equals(previousAuthor, request.Author, StringComparison.Ordinal))
        {
            before["author"] = previousAuthor;
            after["author"] = request.Author;
        }

        if (previousTotalCopies != request.TotalCopies || previousAvailableCopies != newAvailableCopies)
        {
            before["totalCopies"] = previousTotalCopies;
            before["availableCopies"] = previousAvailableCopies;
            after["totalCopies"] = request.TotalCopies;
            after["availableCopies"] = newAvailableCopies;
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor;
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;

        using var payload = JsonSerializer.SerializeToDocument(new JsonObject { ["before"] = before, ["after"] = after });
        var auditEvent = AuditEvent.Create("Book", book.Id, "BookUpdated", resolvedActor, timeProvider.GetUtcNow(), correlationId, payload);
        dbContext.AuditEvents.Add(auditEvent);

        await dbContext.SaveChangesAsync(cancellationToken);

        await CacheReadThrough.RemoveAsync(cache, logger, GetBookAvailability.CacheKey(id), cancellationToken);
        await CacheReadThrough.RemoveAsync(cache, logger, ListBooks.CacheKey(PaginationDefaults.DefaultPage, PaginationDefaults.DefaultPageSize), cancellationToken);

        return TypedResults.Ok(BookResponse.From(book));
    }
}
