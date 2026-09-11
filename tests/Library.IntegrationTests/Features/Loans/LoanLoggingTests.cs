namespace Library.IntegrationTests.Features.Loans;

public class LoanLoggingTests : IAsyncLifetime
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
    public async Task Successful_loan_creation_logs_business_identifiers_as_structured_properties()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var correlationId = Guid.NewGuid().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
        {
            Content = JsonContent.Create(new { bookId = book.Id, userId }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();

        var entry = Assert.Single(_fixture.Logs.Entries, e => e.Message.Contains("created") && e.Message.Contains(loan!.Id.ToString()));

        Assert.Equal(loan!.Id, GetProperty<Guid>(entry, "LoanId"));
        Assert.Equal(book.Id, GetProperty<Guid>(entry, "BookId"));
        Assert.Equal(userId, GetProperty<Guid>(entry, "UserId"));
        Assert.Equal(correlationId, GetProperty<string>(entry, "CorrelationId"));
    }

    private static T GetProperty<T>(CapturedLogEntry entry, string name) =>
        (T)entry.Properties.Single(p => p.Key == name).Value!;
}
