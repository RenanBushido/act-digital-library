namespace Library.IntegrationTests.Features.Books;

[Collection("Api")]
public class BooksEndpointsTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;
    private readonly HttpClient _client;

    public BooksEndpointsTests(ApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object ValidCreateRequest(string isbn = "978-3-16-148410-0", string title = "Clean Code") =>
        new { title, isbn, author = "Robert C. Martin", totalCopies = 3 };

    [Fact]
    public async Task ListBooks_returns_empty_page_on_clean_database()
    {
        var response = await _client.GetAsync("/books");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedBookResponse>();
        Assert.NotNull(body);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task CreateBook_with_valid_body_returns_201()
    {
        var response = await _client.PostAsJsonAsync("/books", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(body);
        Assert.True(body!.IsActive);
        Assert.Equal(body.TotalCopies, body.AvailableCopies);
    }

    [Fact]
    public async Task CreateBook_with_invalid_body_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/books", new { title = "", isbn = "978-3-16-148410-0", author = "Author", totalCopies = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBook_with_duplicate_isbn_returns_409()
    {
        await _client.PostAsJsonAsync("/books", ValidCreateRequest());

        var response = await _client.PostAsJsonAsync("/books", ValidCreateRequest(title: "Clean Code (reprint)"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-isbn-duplicate", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetBook_returns_existing_book_active_or_inactive()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var response = await _client.GetAsync($"/books/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _client.DeleteAsync($"/books/{created.Id}");

        var afterDeactivation = await _client.GetAsync($"/books/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, afterDeactivation.StatusCode);
        var body = await afterDeactivation.Content.ReadFromJsonAsync<BookResponse>();
        Assert.False(body!.IsActive);
    }

    [Fact]
    public async Task GetBook_returns_404_for_nonexistent_book()
    {
        var response = await _client.GetAsync($"/books/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ListBooks_excludes_inactive_books()
    {
        var active = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        var inactive = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest(isbn: "978-0-13-595705-9", title: "The Pragmatic Programmer"))).Content.ReadFromJsonAsync<BookResponse>();
        await _client.DeleteAsync($"/books/{inactive!.Id}");

        var response = await _client.GetAsync("/books");
        var body = await response.Content.ReadFromJsonAsync<PagedBookResponse>();

        Assert.Contains(body!.Items, b => b.Id == active!.Id);
        Assert.DoesNotContain(body.Items, b => b.Id == inactive.Id);
    }

    [Fact]
    public async Task ListBooks_cache_is_invalidated_after_create()
    {
        await _client.GetAsync("/books");

        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var response = await _client.GetAsync("/books");
        var body = await response.Content.ReadFromJsonAsync<PagedBookResponse>();

        Assert.Contains(body!.Items, b => b.Id == created!.Id);
    }

    [Fact]
    public async Task GetBookAvailability_returns_existing_book_active_or_inactive()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var response = await _client.GetAsync($"/books/{created!.Id}/availability");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BookAvailabilityResponse>();
        Assert.True(body!.IsActive);

        await _client.DeleteAsync($"/books/{created.Id}");

        var afterDeactivation = await (await _client.GetAsync($"/books/{created.Id}/availability")).Content.ReadFromJsonAsync<BookAvailabilityResponse>();
        Assert.False(afterDeactivation!.IsActive);
    }

    [Fact]
    public async Task GetBookAvailability_returns_404_for_nonexistent_book()
    {
        var response = await _client.GetAsync($"/books/{Guid.NewGuid()}/availability");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBook_with_valid_body_succeeds()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var response = await _client.PatchAsJsonAsync($"/books/{created!.Id}", new { title = "Clean Code, 2nd Edition", author = "Robert C. Martin", totalCopies = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal("Clean Code, 2nd Edition", body!.Title);
        Assert.Equal(5, body.AvailableCopies);
    }

    [Fact]
    public async Task UpdateBook_reducing_total_copies_below_checked_out_returns_409()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        // Simula 3 exemplares em circulação (sem Loans, é preciso manipular o banco direto para reproduzir o cenário).
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlAsync($"UPDATE books SET available_copies = 0 WHERE \"Id\" = {created!.Id}");
        }

        var response = await _client.PatchAsJsonAsync($"/books/{created!.Id}", new { title = created.Title, author = created.Author, totalCopies = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficient-available-copies", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UpdateBook_on_inactive_book_returns_409()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await _client.DeleteAsync($"/books/{created!.Id}");

        var response = await _client.PatchAsJsonAsync($"/books/{created.Id}", new { title = created.Title, author = created.Author, totalCopies = 5 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-inactive", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UpdateBook_on_nonexistent_book_returns_404()
    {
        var response = await _client.PatchAsJsonAsync($"/books/{Guid.NewGuid()}", new { title = "Title", author = "Author", totalCopies = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateBook_active_book_succeeds()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var response = await _client.DeleteAsync($"/books/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var afterwards = await (await _client.GetAsync($"/books/{created.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.False(afterwards!.IsActive);
    }

    [Fact]
    public async Task DeactivateBook_already_inactive_is_a_noop()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await _client.DeleteAsync($"/books/{created!.Id}");

        var response = await _client.DeleteAsync($"/books/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateBook_on_nonexistent_book_returns_404()
    {
        var response = await _client.DeleteAsync($"/books/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Error_response_echoes_client_correlation_id_in_body_and_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/books/{Guid.NewGuid()}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "client-correlation-id");

        var response = await _client.SendAsync(request);

        Assert.Equal("client-correlation-id", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("client-correlation-id", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Error_response_generates_correlation_id_when_absent()
    {
        var response = await _client.GetAsync($"/books/{Guid.NewGuid()}");

        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(header));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(header, problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Success_response_echoes_correlation_id_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/books");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "client-correlation-id");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("client-correlation-id", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Success_response_generates_correlation_id_when_absent()
    {
        var response = await _client.GetAsync("/books");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(header));
    }
}
