namespace Library.Api.Infrastructure.Caching;

public sealed record BookListFilter(string? Title, string? Author, string? Isbn, bool IncludeInactive, int Page, int PageSize)
{
    public string ComputeHash()
    {
        var normalized = string.Join(
            '|',
            NormalizeText(Title),
            NormalizeText(Author),
            NormalizeText(Isbn),
            IncludeInactive ? "1" : "0",
            Page.ToString(CultureInfo.InvariantCulture),
            PageSize.ToString(CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    private static string NormalizeText(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
