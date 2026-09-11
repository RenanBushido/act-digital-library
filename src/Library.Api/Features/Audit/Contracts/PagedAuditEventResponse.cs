namespace Library.Api.Features.Audit.Contracts;

public sealed record PagedAuditEventResponse(IReadOnlyList<AuditEventResponse> Items, int Page, int PageSize, int TotalCount)
{
    public static PagedAuditEventResponse From(PagedResult<AuditEvent> paged) => new(
        [.. paged.Items.Select(AuditEventResponse.From)],
        paged.Page,
        paged.PageSize,
        paged.TotalCount);
}
