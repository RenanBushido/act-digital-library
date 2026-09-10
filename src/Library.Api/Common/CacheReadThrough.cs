namespace Library.Api.Common;

public static class CacheReadThrough
{
    public static async Task<T?> GetOrCreateAsync<T>(
        IDistributedCache cache,
        ILogger logger,
        string key,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<T>(cached);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed for key {CacheKey}; falling back to source.", key);
        }

        var value = await factory(cancellationToken);

        if (value is not null)
        {
            try
            {
                await cache.SetStringAsync(key, JsonSerializer.Serialize(value), CacheDefaults.Options, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache write failed for key {CacheKey}.", key);
            }
        }

        return value;
    }

    public static async Task RemoveAsync(IDistributedCache cache, ILogger logger, string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache invalidation failed for key {CacheKey}.", key);
        }
    }
}
