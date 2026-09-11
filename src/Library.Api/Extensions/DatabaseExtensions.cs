namespace Library.Api.Extensions;

public static class DatabaseExtensions
{
    public static IServiceCollection AddApiDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        // A connection string é lida dentro do delegate (não capturada antes), porque `AddDbContext`
        // só invoca esse delegate quando o DbContext é resolvido pela primeira vez — depois que toda
        // a configuração (incluindo overrides de teste do WebApplicationFactory) já foi aplicada.
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Connection string 'Postgres' not found.");

            // Fixado em código (não deixado para cada connection string) para que nenhum ambiente
            // suba com o pool default do Npgsql (100): com 11 réplicas no máximo do desafio,
            // 11 x 8 = 88 conexões, abaixo do max_connections padrão do PostgreSQL (100).
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = 8 };

            options.UseNpgsql(connectionStringBuilder.ConnectionString);
        });

        return services;
    }
}
