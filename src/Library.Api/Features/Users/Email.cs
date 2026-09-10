namespace Library.Api.Features.Users;

public readonly record struct Email
{
    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string rawValue)
    {
        var normalized = Normalize(rawValue);

        return string.IsNullOrWhiteSpace(normalized) ? throw new DomainException("E-mail cannot be empty.") : new Email(normalized);
    }

    public static string Normalize(string? rawValue) =>
        (rawValue ?? string.Empty).Trim().ToLowerInvariant();

    public override string ToString() => Value;
}
