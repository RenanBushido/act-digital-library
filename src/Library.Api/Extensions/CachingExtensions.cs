namespace Library.Api.Extensions;

public static class CachingExtensions
{
    public static IServiceCollection AddApiCaching(this IServiceCollection services, IConfiguration configuration)
    {
        // A connection string é lida dentro do delegate (não capturada antes), pelo mesmo motivo
        // de `AddApiDatabase`: `IOptions<RedisCacheOptions>` só é resolvido na primeira vez que o
        // cache é usado, depois que os overrides de configuração de teste já foram aplicados.
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Connection string 'Redis' not found.");
        });

        return services;
    }
}
