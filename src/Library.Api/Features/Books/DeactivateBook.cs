namespace Library.Api.Features.Books;

public static class DeactivateBook
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext httpContext,
        [FromHeader(Name = "X-Actor")] string? actor,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        BookCache bookCache,
        CancellationToken cancellationToken)
    {
        var book = await dbContext.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (book is null)
        {
            return BookErrors.NotFound(id).ToProblem();
        }

        var wasActive = book.IsActive;

        book.Deactivate(timeProvider);

        // O escopo de `payload` precisa envolver o `SaveChangesAsync` abaixo: um `using` preso
        // ao bloco `if` descartaria o JsonDocument antes do Npgsql serializar o jsonb.
        using var payload = wasActive
            ? JsonSerializer.SerializeToDocument(new
            {
                before = new { isActive = true },
                after = new { isActive = false },
            })
            : null;

        if (wasActive)
        {
            var resolvedActor = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor;
            var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;

            var auditEvent = AuditEvent.Create("Book", book.Id, "BookDeactivated", resolvedActor, timeProvider.GetUtcNow(), correlationId, payload!);
            dbContext.AuditEvents.Add(auditEvent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasActive)
        {
            await bookCache.InvalidateAvailabilityAsync(id, cancellationToken);
            await bookCache.InvalidateListAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }
}
