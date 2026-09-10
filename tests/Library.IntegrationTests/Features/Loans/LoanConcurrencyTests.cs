namespace Library.IntegrationTests.Features.Loans;

[Collection("Api")]
public class LoanConcurrencyTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int ConcurrentRequests = 20;

    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Last_copy_under_concurrent_load_produces_exactly_one_success()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var userIds = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(_ => LoanTestHelpers.CreateUserAsync(_fixture)));

        using var barrier = new Barrier(ConcurrentRequests);

        var tasks = userIds.Select(userId => Task.Run(async () =>
        {
            using var client = _fixture.CreateClient();
            barrier.SignalAndWait();

            using var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
            {
                Content = JsonContent.Create(new { bookId = book.Id, userId }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

            return await client.SendAsync(request);
        }));

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(ConcurrentRequests - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("no-copy-available", problem.GetProperty("type").GetString());
        }

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(0, bookAfter!.AvailableCopies);

        var history = await (await _client.GetAsync($"/books/{book.Id}/history")).Content.ReadFromJsonAsync<PagedLoanResponse>();
        Assert.Single(history!.Items, l => l.Status == "Active");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
