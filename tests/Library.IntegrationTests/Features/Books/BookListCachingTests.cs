namespace Library.IntegrationTests.Features.Books;

[Collection("Api")]
public class BookListCachingTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Title_filter_restricts_the_result_to_matching_books()
    {
        await CreateBookAsync(title: "Clean Code", isbn: "978-3-16-148410-0");
        await CreateBookAsync(title: "Refactoring", isbn: "978-0-13-475759-9");

        var body = await GetListAsync("/books?title=Clean");

        Assert.Single(body.Items);
        Assert.Equal("Clean Code", body.Items[0].Title);
    }

    [Fact]
    public async Task Author_filter_restricts_the_result_to_matching_books()
    {
        await CreateBookAsync(title: "Clean Code", author: "Robert C. Martin", isbn: "978-3-16-148410-0");
        await CreateBookAsync(title: "Refactoring", author: "Martin Fowler", isbn: "978-0-13-475759-9");

        var body = await GetListAsync("/books?author=Fowler");

        Assert.Single(body.Items);
        Assert.Equal("Refactoring", body.Items[0].Title);
    }

    [Fact]
    public async Task Isbn_filter_restricts_the_result_to_the_matching_book()
    {
        var created = await CreateBookAsync(title: "Clean Code", isbn: "978-3-16-148410-0");
        await CreateBookAsync(title: "Refactoring", isbn: "978-0-13-475759-9");

        var body = await GetListAsync("/books?isbn=978-3-16-148410-0");

        Assert.Single(body.Items);
        Assert.Equal(created.Id, body.Items[0].Id);
    }

    [Fact]
    public async Task Isbn_filter_that_normalizes_to_empty_returns_400_instead_of_500()
    {
        var response = await _client.GetAsync("/books?isbn=---");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation-failed", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task IncludeInactive_filter_brings_back_deactivated_books()
    {
        var inactive = await CreateBookAsync(title: "Clean Code", isbn: "978-3-16-148410-0");
        await _client.DeleteAsync($"/books/{inactive.Id}");

        var withoutFilter = await GetListAsync("/books");
        var withFilter = await GetListAsync("/books?includeInactive=true");

        Assert.DoesNotContain(withoutFilter.Items, b => b.Id == inactive.Id);
        Assert.Contains(withFilter.Items, b => b.Id == inactive.Id);
    }

    [Fact]
    public async Task Second_read_within_the_ttl_is_served_from_cache()
    {
        var created = await CreateBookAsync(title: "Clean Code", isbn: "978-3-16-148410-0");

        var first = await GetListAsync("/books");
        Assert.Contains(first.Items, b => b.Id == created.Id);

        // Desativa direto no banco (sem passar por DELETE /books/{id}, que invalidaria o cache),
        // para provar que a segunda leitura veio do Redis.
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlAsync($"UPDATE books SET is_active = false WHERE \"Id\" = {created.Id}");
        }

        var second = await GetListAsync("/books");
        Assert.Contains(second.Items, b => b.Id == created.Id);
    }

    [Fact]
    public async Task Different_filter_combinations_are_cached_independently()
    {
        await CreateBookAsync(title: "Clean Code", author: "Robert C. Martin", isbn: "978-3-16-148410-0");
        await CreateBookAsync(title: "Refactoring", author: "Martin Fowler", isbn: "978-0-13-475759-9");

        var byTitle = await GetListAsync("/books?title=Clean");
        var byAuthor = await GetListAsync("/books?author=Fowler");

        Assert.Single(byTitle.Items);
        Assert.Equal("Clean Code", byTitle.Items[0].Title);

        Assert.Single(byAuthor.Items);
        Assert.Equal("Refactoring", byAuthor.Items[0].Title);
    }

    [Fact]
    public async Task Invalidation_after_a_book_is_created_reaches_every_cached_filter_combination()
    {
        await CreateBookAsync(title: "Clean Code", isbn: "978-3-16-148410-0");

        await GetListAsync("/books");
        await GetListAsync("/books?title=Refactoring");

        var newBook = await CreateBookAsync(title: "Refactoring", isbn: "978-0-13-475759-9");

        var defaultList = await GetListAsync("/books");
        var filteredList = await GetListAsync("/books?title=Refactoring");

        Assert.Contains(defaultList.Items, b => b.Id == newBook.Id);
        Assert.Contains(filteredList.Items, b => b.Id == newBook.Id);
    }

    private async Task<BookResponse> CreateBookAsync(string title, string isbn, string author = "Robert C. Martin", int totalCopies = 3)
    {
        var response = await _client.PostAsJsonAsync("/books", new { title, isbn, author, totalCopies });
        return (await response.Content.ReadFromJsonAsync<BookResponse>())!;
    }

    private async Task<PagedBookResponse> GetListAsync(string requestUri) =>
        (await (await _client.GetAsync(requestUri)).Content.ReadFromJsonAsync<PagedBookResponse>())!;
}
