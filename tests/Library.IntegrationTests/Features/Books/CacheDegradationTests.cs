namespace Library.IntegrationTests.Features.Books;

public class CacheDegradationTests : IAsyncLifetime
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
    public async Task GetBooks_and_availability_still_respond_when_redis_is_unavailable()
    {
        var created = await (await _client.PostAsJsonAsync("/books", new { title = "Clean Code", isbn = "978-3-16-148410-0", author = "Robert C. Martin", totalCopies = 3 }))
            .Content.ReadFromJsonAsync<BookResponse>();

        await _fixture.StopRedisAsync();

        var listResponse = await _client.GetAsync("/books");
        var availabilityResponse = await _client.GetAsync($"/books/{created!.Id}/availability");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, availabilityResponse.StatusCode);
    }
}
