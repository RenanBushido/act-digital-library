namespace Library.Api.Common;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public int AvailabilityTtlSeconds { get; set; } = 60;
    public int ListTtlSeconds { get; set; } = 120;
}
