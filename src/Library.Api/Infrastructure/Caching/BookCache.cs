namespace Library.Api.Infrastructure.Caching;

public sealed class BookCache(
    IDistributedCache cache,
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<CacheOptions> options,
    ILogger<BookCache> logger)
{
    private const string KeyPrefix = "library:";
    private const string ListVersionKey = $"{KeyPrefix}books:list:version";

    public Task<BookAvailabilityResponse?> GetAvailabilityAsync(Guid bookId, CancellationToken cancellationToken) =>
        GetAsync<BookAvailabilityResponse>(AvailabilityKey(bookId), cancellationToken);

    public Task SetAvailabilityAsync(Guid bookId, BookAvailabilityResponse value, CancellationToken cancellationToken) =>
        SetAsync(AvailabilityKey(bookId), value, TimeSpan.FromSeconds(options.Value.AvailabilityTtlSeconds), cancellationToken);

    public Task InvalidateAvailabilityAsync(Guid bookId, CancellationToken cancellationToken) =>
        RemoveAsync(AvailabilityKey(bookId), cancellationToken);

    public async Task<PagedBookResponse?> GetListAsync(BookListFilter filter, CancellationToken cancellationToken)
    {
        var key = await BuildListKeyAsync(filter, cancellationToken);
        return await GetAsync<PagedBookResponse>(key, cancellationToken);
    }

    public async Task SetListAsync(BookListFilter filter, PagedBookResponse value, CancellationToken cancellationToken)
    {
        var key = await BuildListKeyAsync(filter, cancellationToken);
        await SetAsync(key, value, TimeSpan.FromSeconds(options.Value.ListTtlSeconds), cancellationToken);
    }

    public async Task InvalidateListAsync(CancellationToken cancellationToken)
    {
        try
        {
            var database = connectionMultiplexer.GetDatabase();
            await database.StringIncrementAsync(ListVersionKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache invalidation failed for key {CacheKey}.", ListVersionKey);
        }
    }

    private static string AvailabilityKey(Guid bookId) => $"{KeyPrefix}book:{bookId}:availability";

    private async Task<string> BuildListKeyAsync(BookListFilter filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var version = await GetListVersionAsync();
        return $"{KeyPrefix}books:list:v{version}:{filter.ComputeHash()}";
    }

    private async Task<long> GetListVersionAsync()
    {
        try
        {
            var database = connectionMultiplexer.GetDatabase();
            var value = await database.StringGetAsync(ListVersionKey);
            return value.HasValue ? (long)value : 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed for key {CacheKey}; falling back to version 0.", ListVersionKey);
            return 0;
        }
    }

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            return cached is null ? null : JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed for key {CacheKey}; falling back to source.", key);
            return null;
        }
    }

    private async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
            await cache.SetStringAsync(key, JsonSerializer.Serialize(value), entryOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for key {CacheKey}.", key);
        }
    }

    private async Task RemoveAsync(string key, CancellationToken cancellationToken)
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
