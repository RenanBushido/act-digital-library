namespace Library.IntegrationTests.Features.Books;

[Collection("Api")]
public class BookAvailabilityCachingTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object ValidCreateRequest(string isbn = "978-3-16-148410-0", string title = "Clean Code", int totalCopies = 3) =>
        new { title, isbn, author = "Robert C. Martin", totalCopies };

    [Fact]
    public async Task Second_read_within_the_ttl_is_served_from_cache()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var first = await (await _client.GetAsync($"/books/{created!.Id}/availability")).Content.ReadFromJsonAsync<BookAvailabilityResponse>();
        Assert.Equal(3, first!.AvailableCopies);

        // O banco muda direto (sem passar por um endpoint de escrita, que invalidaria o cache),
        // para provar que a segunda leitura veio do Redis e não do PostgreSQL.
        await UpdateAvailableCopiesDirectlyAsync(created.Id, 0);

        var second = await (await _client.GetAsync($"/books/{created.Id}/availability")).Content.ReadFromJsonAsync<BookAvailabilityResponse>();
        Assert.Equal(3, second!.AvailableCopies);
    }

    [Fact]
    public async Task Read_after_update_reflects_the_new_state()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await _client.GetAsync($"/books/{created!.Id}/availability");

        await _client.PatchAsJsonAsync(
            $"/books/{created.Id}",
            new { title = created.Title, author = created.Author, totalCopies = 5 });

        var afterUpdate = await (await _client.GetAsync($"/books/{created.Id}/availability")).Content.ReadFromJsonAsync<BookAvailabilityResponse>();
        Assert.Equal(5, afterUpdate!.TotalCopies);
        Assert.Equal(5, afterUpdate.AvailableCopies);
    }

    [Fact]
    public async Task Read_after_deactivation_reflects_the_new_state()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await _client.GetAsync($"/books/{created!.Id}/availability");

        await _client.DeleteAsync($"/books/{created.Id}");

        var afterDeactivation = await (await _client.GetAsync($"/books/{created.Id}/availability")).Content.ReadFromJsonAsync<BookAvailabilityResponse>();
        Assert.False(afterDeactivation!.IsActive);
    }

    [Fact]
    public async Task Poisoned_availability_cache_never_overrides_the_database_for_a_loan_decision()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await UpdateAvailableCopiesDirectlyAsync(created!.Id, 0);

        await PoisonAvailabilityCacheAsync(created.Id, totalCopies: 3, availableCopies: 3, isActive: true);

        var userId = await Library.IntegrationTests.Features.Loans.LoanTestHelpers.CreateUserAsync(_fixture);
        var response = await Library.IntegrationTests.Features.Loans.LoanTestHelpers.PostLoanAsync(_client, created.Id, userId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no-copy-available", problem.GetProperty("type").GetString());

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loanCount = await dbContext.Loans.CountAsync(l => l.BookId == created.Id);
        Assert.Equal(0, loanCount);
    }

    private async Task UpdateAvailableCopiesDirectlyAsync(Guid bookId, int availableCopies)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlAsync($"UPDATE books SET available_copies = {availableCopies} WHERE \"Id\" = {bookId}");
    }

    private async Task PoisonAvailabilityCacheAsync(Guid bookId, int totalCopies, int availableCopies, bool isActive)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var connectionMultiplexer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var database = connectionMultiplexer.GetDatabase();

        var poisonedValue = JsonSerializer.Serialize(new BookAvailabilityResponse(totalCopies, availableCopies, isActive));
        await database.StringSetAsync($"library:book:{bookId}:availability", poisonedValue);
    }
}
