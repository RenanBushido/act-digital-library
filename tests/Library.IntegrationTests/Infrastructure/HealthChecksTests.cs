namespace Library.IntegrationTests.Infrastructure;

public class HealthChecksTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)_fixture).InitializeAsync();
        _client = _fixture.CreateClient();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_fixture).DisposeAsync();

    [Fact]
    public async Task Liveness_and_readiness_respond_200_when_postgres_and_redis_are_up()
    {
        var live = await _client.GetAsync("/health/live");
        var ready = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Liveness_still_responds_200_when_postgres_is_down()
    {
        await _fixture.StopPostgresAsync();

        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_still_responds_200_when_postgres_and_redis_are_down()
    {
        await _fixture.StopPostgresAsync();
        await _fixture.StopRedisAsync();

        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_responds_503_and_identifies_postgres_as_unhealthy_when_postgres_is_down()
    {
        await _fixture.StopPostgresAsync();

        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unhealthy", body.GetProperty("checks").GetProperty("postgres").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_responds_200_and_identifies_redis_as_degraded_when_only_redis_is_down()
    {
        await _fixture.StopRedisAsync();

        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Degraded", body.GetProperty("checks").GetProperty("redis").GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("postgres").GetProperty("status").GetString());
    }
}
