namespace Library.Api.Features.Books;

public sealed record CreateBookRequest(
    [property: Required, MinLength(1)] string Title,
    [property: Required, MinLength(1)] string Isbn,
    [property: Required, MinLength(1)] string Author,
    [property: Range(1, int.MaxValue)] int TotalCopies);

public static class CreateBook
{
    public static async Task<IResult> HandleAsync(
        CreateBookRequest request,
        HttpContext httpContext,
        [FromHeader(Name = "X-Actor")] string? actor,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        BookCache bookCache,
        CancellationToken cancellationToken)
    {
        Book book;
        try
        {
            book = Book.Create(request.Title, request.Isbn, request.Author, request.TotalCopies, request.TotalCopies, timeProvider);
        }
        catch (DomainException ex)
        {
            return BookErrors.ValidationFailed(ex.Message).ToProblem();
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor;
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;

        using var payload = JsonSerializer.SerializeToDocument(new
        {
            after = new
            {
                title = book.Title,
                isbn = book.Isbn.Value,
                author = book.Author,
                totalCopies = book.TotalCopies,
                availableCopies = book.AvailableCopies,
            },
        });
        var auditEvent = AuditEvent.Create("Book", book.Id, "BookCreated", resolvedActor, timeProvider.GetUtcNow(), correlationId, payload);

        dbContext.Books.Add(book);
        dbContext.AuditEvents.Add(auditEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return BookErrors.IsbnDuplicate(request.Isbn).ToProblem();
        }

        await bookCache.InvalidateListAsync(cancellationToken);

        return TypedResults.Created($"/books/{book.Id}", BookResponse.From(book));
    }
}
