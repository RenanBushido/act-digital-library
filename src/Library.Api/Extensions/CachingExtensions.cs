namespace Library.Api.Extensions;

public static class CachingExtensions
{
    public static IServiceCollection AddApiCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        // A connection string é lida dentro do delegate (não capturada antes), pelo mesmo motivo
        // de `AddApiDatabase`: o singleton só é construído na primeira vez que é resolvido, depois
        // que os overrides de configuração de teste já foram aplicados.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connectionString = configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Connection string 'Redis' not found.");
            return ConnectionMultiplexer.Connect(connectionString);
        });

        // Sem `InstanceName`: ele prefixa só as chaves do IDistributedCache, não as do
        // IConnectionMultiplexer, criando duas convenções de prefixo. `BookCache` escreve o
        // prefixo `library:` explicitamente nas duas chaves.
        services.AddStackExchangeRedisCache(_ => { });

        // `ConnectionMultiplexerFactory` compartilha a mesma conexão registrada acima, em vez de
        // deixar o AddStackExchangeRedisCache abrir a sua própria.
        services.AddOptions<RedisCacheOptions>()
            .Configure<IConnectionMultiplexer>((options, multiplexer) =>
            {
                options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer);
            });

        services.AddSingleton<BookCache>();

        return services;
    }
}
