namespace Library.IntegrationTests.Infrastructure;

public class MigrationModeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine3.24")
        .WithDatabase("library")
        .WithUsername("library")
        .WithPassword("localdev")
        .Build();

    // Só usado pelo teste que sobe o host completo: o registro de tracing resolve o
    // `IConnectionMultiplexer` do DI já no startup (`AddRedisInstrumentation`), então precisa de
    // um Redis alcançável mesmo num teste que não exercita nenhum caminho de cache.
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.1-alpine").Build();

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task RunMigrateOnlyAsync_applies_pending_migrations_and_returns_zero()
    {
        var exitCode = await Program.RunMigrateOnlyAsync([$"--ConnectionStrings:Postgres={_postgres.GetConnectionString()}"]);

        Assert.Equal(0, exitCode);
        Assert.True(await LoansTableExistsAsync());
    }

    [Fact]
    public async Task RunMigrateOnlyAsync_with_database_unreachable_returns_nonzero()
    {
        var connectionString = _postgres.GetConnectionString();
        await _postgres.StopAsync();

        var exitCode = await Program.RunMigrateOnlyAsync([$"--ConnectionStrings:Postgres={connectionString}"]);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task App_started_without_the_argument_and_with_migrate_on_startup_disabled_does_not_migrate()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                    ["Database:MigrateOnStartup"] = "false",
                });
            }));

        using var client = factory.CreateClient();
        await client.GetAsync("/health/live");

        Assert.False(await LoansTableExistsAsync());
    }

    [Fact]
    public async Task App_started_without_the_argument_and_with_migrate_on_startup_enabled_migrates_before_serving_traffic()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                    ["Database:MigrateOnStartup"] = "true",
                });
            }));

        // A migration roda antes de `app.Run()` no próprio Program.cs; `CreateClient()` já força o
        // host (e, portanto, esse trecho) a terminar de subir antes de devolver o cliente.
        using var client = factory.CreateClient();

        Assert.True(await LoansTableExistsAsync());

        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<bool> LoansTableExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.loans') IS NOT NULL;";
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
