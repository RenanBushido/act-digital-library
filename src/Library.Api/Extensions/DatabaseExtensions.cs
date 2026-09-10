namespace Library.Api.Extensions;

public static class DatabaseExtensions
{
    public static IServiceCollection AddApiDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Connection string 'Postgres' not found.");
        services.AddDbContext<LibraryDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
