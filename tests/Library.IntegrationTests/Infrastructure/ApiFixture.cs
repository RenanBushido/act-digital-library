namespace Library.IntegrationTests.Infrastructure;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine3.24")
        .WithDatabase("library")
        .WithUsername("library")
        .WithPassword("localdev")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.1-alpine").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
            });
        });
    }

    // O TestServer só serve HTTP; sem isso, `app.UseHttpsRedirection()` redireciona toda
    // requisição e o HttpClient (que segue redirect por padrão) reenvia a chamada,
    // duplicando POST/PATCH/DELETE.
    protected override void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://localhost");
        base.ConfigureClient(client);
    }

    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE books, users, loans, audit_events RESTART IDENTITY CASCADE;");
    }

    public async Task StopRedisAsync() => await _redis.StopAsync();
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
