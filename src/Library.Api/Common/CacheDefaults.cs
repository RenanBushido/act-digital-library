namespace Library.Api.Common;

public static class CacheDefaults
{
    public static readonly DistributedCacheEntryOptions Options = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60),
    };
}
