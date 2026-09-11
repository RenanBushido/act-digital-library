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

    [Fact]
    public async Task Book_and_loan_writes_still_succeed_when_redis_is_unavailable_during_invalidation()
    {
        var created = await (await _client.PostAsJsonAsync("/books", new { title = "Clean Code", isbn = "978-3-16-148410-0", author = "Robert C. Martin", totalCopies = 3 }))
            .Content.ReadFromJsonAsync<BookResponse>();

        var userId = await Library.IntegrationTests.Features.Loans.LoanTestHelpers.CreateUserAsync(_fixture);

        await _fixture.StopRedisAsync();

        var updateResponse = await _client.PatchAsJsonAsync(
            $"/books/{created!.Id}",
            new { title = created.Title, author = created.Author, totalCopies = 5 });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var loanResponse = await Library.IntegrationTests.Features.Loans.LoanTestHelpers.PostLoanAsync(_client, created.Id, userId);
        Assert.Equal(HttpStatusCode.Created, loanResponse.StatusCode);
        var loan = await loanResponse.Content.ReadFromJsonAsync<LoanResponse>();

        var returnResponse = await _client.PostAsync($"/loans/{loan!.Id}/return", content: null);
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        var secondLoanResponse = await Library.IntegrationTests.Features.Loans.LoanTestHelpers.PostLoanAsync(_client, created.Id, userId);
        Assert.Equal(HttpStatusCode.Created, secondLoanResponse.StatusCode);
        var secondLoan = await secondLoanResponse.Content.ReadFromJsonAsync<LoanResponse>();

        var cancelResponse = await _client.PostAsync($"/loans/{secondLoan!.Id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var deactivateResponse = await _client.DeleteAsync($"/books/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);
    }
}
