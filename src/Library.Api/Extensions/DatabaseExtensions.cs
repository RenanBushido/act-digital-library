namespace Library.Api.Extensions;

public static class DatabaseExtensions
{
    public static IServiceCollection AddApiDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        // A connection string é lida dentro do delegate (não capturada antes), porque `AddDbContext`
        // só invoca esse delegate quando o DbContext é resolvido pela primeira vez — depois que toda
        // a configuração (incluindo overrides de teste do WebApplicationFactory) já foi aplicada.
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Connection string 'Postgres' not found.")));

        return services;
    }
}
