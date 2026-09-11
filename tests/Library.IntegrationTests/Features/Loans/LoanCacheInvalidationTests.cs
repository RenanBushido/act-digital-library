namespace Library.IntegrationTests.Features.Loans;

[Collection("Api")]
public class LoanCacheInvalidationTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creating_a_loan_invalidates_the_cached_availability_of_the_book()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 3);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        await GetAvailabilityAsync(book.Id);

        await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        var afterCreate = await GetAvailabilityAsync(book.Id);
        Assert.Equal(2, afterCreate.AvailableCopies);
    }

    [Fact]
    public async Task Creating_a_loan_invalidates_the_cached_listing()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 3);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        await GetListAsync();

        await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        var afterCreate = await GetListAsync();
        var listedBook = afterCreate.Items.Single(b => b.Id == book.Id);
        Assert.Equal(2, listedBook.AvailableCopies);
    }

    [Fact]
    public async Task Returning_a_loan_invalidates_the_cached_availability_of_the_book()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        await GetAvailabilityAsync(book.Id);

        await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        var afterReturn = await GetAvailabilityAsync(book.Id);
        Assert.Equal(1, afterReturn.AvailableCopies);
    }

    [Fact]
    public async Task Returning_a_loan_invalidates_the_cached_listing()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        await GetListAsync();

        await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        var afterReturn = await GetListAsync();
        var listedBook = afterReturn.Items.Single(b => b.Id == book.Id);
        Assert.Equal(1, listedBook.AvailableCopies);
    }

    [Fact]
    public async Task Cancelling_a_loan_invalidates_the_cached_availability_of_the_book()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        await GetAvailabilityAsync(book.Id);

        await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        var afterCancel = await GetAvailabilityAsync(book.Id);
        Assert.Equal(1, afterCancel.AvailableCopies);
    }

    [Fact]
    public async Task Cancelling_a_loan_invalidates_the_cached_listing()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        await GetListAsync();

        await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        var afterCancel = await GetListAsync();
        var listedBook = afterCancel.Items.Single(b => b.Id == book.Id);
        Assert.Equal(1, listedBook.AvailableCopies);
    }

    private async Task<BookAvailabilityResponse> GetAvailabilityAsync(Guid bookId) =>
        (await (await _client.GetAsync($"/books/{bookId}/availability")).Content.ReadFromJsonAsync<BookAvailabilityResponse>())!;

    private async Task<PagedBookResponse> GetListAsync() =>
        (await (await _client.GetAsync("/books")).Content.ReadFromJsonAsync<PagedBookResponse>())!;
}
