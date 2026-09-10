namespace Library.IntegrationTests.Features.Loans;

[Collection("Api")]
public class LoanHistoryTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetBookHistory_returns_page_with_active_returned_and_cancelled_loans()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 3);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var active = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        var returned = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        var cancelled = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.PostAsync($"/loans/{returned.Id}/return", content: null);
        await _client.PostAsync($"/loans/{cancelled.Id}/cancel", content: null);

        var response = await _client.GetAsync($"/books/{book.Id}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedLoanResponse>();
        Assert.Equal(3, page!.TotalCount);
        Assert.Contains(page.Items, l => l.Id == active.Id && l.Status == "Active");
        Assert.Contains(page.Items, l => l.Id == returned.Id && l.Status == "Returned");
        Assert.Contains(page.Items, l => l.Id == cancelled.Id && l.Status == "Cancelled");
    }

    [Fact]
    public async Task GetBookHistory_for_nonexistent_book_returns_404()
    {
        var response = await _client.GetAsync($"/books/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetUserLoans_returns_page_with_active_returned_and_cancelled_loans()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 3);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var active = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        var returned = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        var cancelled = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.PostAsync($"/loans/{returned.Id}/return", content: null);
        await _client.PostAsync($"/loans/{cancelled.Id}/cancel", content: null);

        var response = await _client.GetAsync($"/users/{userId}/loans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedLoanResponse>();
        Assert.Equal(3, page!.TotalCount);
        Assert.Contains(page.Items, l => l.Id == active.Id && l.Status == "Active");
        Assert.Contains(page.Items, l => l.Id == returned.Id && l.Status == "Returned");
        Assert.Contains(page.Items, l => l.Id == cancelled.Id && l.Status == "Cancelled");
    }

    [Fact]
    public async Task GetUserLoans_for_nonexistent_user_returns_404()
    {
        var response = await _client.GetAsync($"/users/{Guid.NewGuid()}/loans");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("user-not-found", problem.GetProperty("type").GetString());
    }
}
