namespace Library.Api.Features.Books;

public readonly record struct Isbn
{
    public string Value { get; }

    private Isbn(string value)
    {
        Value = value;
    }

    public static Isbn Create(string rawValue)
    {
        var normalized = Normalize(rawValue);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainException("ISBN não pode ser vazio.");
        }

        return new Isbn(normalized);
    }

    public static string Normalize(string? rawValue) =>
        (rawValue ?? string.Empty).Replace("-", string.Empty).Trim().ToUpperInvariant();

    public override string ToString() => Value;
}
