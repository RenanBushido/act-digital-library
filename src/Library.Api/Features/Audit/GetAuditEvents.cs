namespace Library.Api.Features.Audit;

public static class GetAuditEvents
{
    public static async Task<IResult> HandleAsync(
        string? entityType,
        Guid? entityId,
        string? action,
        string? actor,
        string? correlationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page is > 0 ? page.Value : PaginationDefaults.DefaultPage;
        var resolvedPageSize = pageSize is > 0 and <= PaginationDefaults.MaxPageSize ? pageSize.Value : PaginationDefaults.DefaultPageSize;

        var query = dbContext.AuditEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (entityId is not null)
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            query = query.Where(e => e.Actor == actor);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(e => e.CorrelationId == correlationId);
        }

        if (from is not null)
        {
            query = query.Where(e => e.OccurredAtUtc >= from);
        }

        if (to is not null)
        {
            query = query.Where(e => e.OccurredAtUtc <= to);
        }

        query = query.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);

        var response = PagedAuditEventResponse.From(new PagedResult<AuditEvent>(items, resolvedPage, resolvedPageSize, totalCount));

        return TypedResults.Ok(response);
    }
}
