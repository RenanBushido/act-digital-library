namespace Library.IntegrationTests.Features.Books;

[Collection("Api")]
public class BooksEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

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

    [Fact]
    public async Task CreateBook_successful_creation_writes_a_single_BookCreated_event_with_standard_payload()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var events = await GetAuditEventsAsync(created!.Id);

        var auditEvent = Assert.Single(events);
        Assert.Equal("BookCreated", auditEvent.Action);
        Assert.Equal("Book", auditEvent.EntityType);
        Assert.Equal("anonymous", auditEvent.Actor);
        var payload = auditEvent.Payload.RootElement;
        Assert.False(payload.TryGetProperty("before", out _));
        var after = payload.GetProperty("after");
        Assert.Equal("Clean Code", after.GetProperty("title").GetString());
        Assert.Equal(3, after.GetProperty("totalCopies").GetInt32());
        Assert.Equal(3, after.GetProperty("availableCopies").GetInt32());
    }

    [Fact]
    public async Task CreateBook_with_duplicate_isbn_does_not_leave_an_orphan_event()
    {
        await _client.PostAsJsonAsync("/books", ValidCreateRequest());

        var response = await _client.PostAsJsonAsync("/books", ValidCreateRequest(title: "Clean Code (reprint)"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await dbContext.AuditEvents.CountAsync(e => e.Action == "BookCreated");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateBook_with_invalid_body_does_not_leave_an_orphan_event()
    {
        var response = await _client.PostAsJsonAsync("/books", new { title = "", isbn = "978-3-16-148410-0", author = "Author", totalCopies = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await dbContext.AuditEvents.CountAsync(e => e.Action == "BookCreated");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateBook_changing_total_copies_audits_before_and_after_of_quantity_fields()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        await _client.PatchAsJsonAsync($"/books/{created!.Id}", new { title = created.Title, author = created.Author, totalCopies = 5 });

        var events = await GetAuditEventsAsync(created.Id);
        var updated = Assert.Single(events, e => e.Action == "BookUpdated");
        var payload = updated.Payload.RootElement;
        Assert.Equal(3, payload.GetProperty("before").GetProperty("totalCopies").GetInt32());
        Assert.Equal(3, payload.GetProperty("before").GetProperty("availableCopies").GetInt32());
        Assert.Equal(5, payload.GetProperty("after").GetProperty("totalCopies").GetInt32());
        Assert.Equal(5, payload.GetProperty("after").GetProperty("availableCopies").GetInt32());
        Assert.False(payload.GetProperty("before").TryGetProperty("title", out _));
    }

    [Fact]
    public async Task UpdateBook_changing_only_the_title_audits_before_and_after_of_the_title_alone()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        await _client.PatchAsJsonAsync(
            $"/books/{created!.Id}",
            new { title = "Clean Code, 2nd Edition", author = created.Author, totalCopies = created.TotalCopies });

        var events = await GetAuditEventsAsync(created.Id);
        var updated = Assert.Single(events, e => e.Action == "BookUpdated");
        var payload = updated.Payload.RootElement;
        Assert.Equal(created.Title, payload.GetProperty("before").GetProperty("title").GetString());
        Assert.Equal("Clean Code, 2nd Edition", payload.GetProperty("after").GetProperty("title").GetString());
        Assert.False(payload.GetProperty("before").TryGetProperty("author", out _));
        Assert.False(payload.GetProperty("before").TryGetProperty("totalCopies", out _));
        Assert.False(payload.GetProperty("before").TryGetProperty("availableCopies", out _));
    }

    [Fact]
    public async Task UpdateBook_with_no_effective_field_change_is_still_audited_with_empty_payload()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        var response = await _client.PatchAsJsonAsync(
            $"/books/{created!.Id}",
            new { title = created.Title, author = created.Author, totalCopies = created.TotalCopies });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await GetAuditEventsAsync(created.Id);
        var updated = Assert.Single(events, e => e.Action == "BookUpdated");
        var payload = updated.Payload.RootElement;
        Assert.Empty(payload.GetProperty("before").EnumerateObject());
        Assert.Empty(payload.GetProperty("after").EnumerateObject());
    }

    [Fact]
    public async Task UpdateBook_on_inactive_book_does_not_leave_an_orphan_event()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await _client.DeleteAsync($"/books/{created!.Id}");

        var response = await _client.PatchAsJsonAsync($"/books/{created.Id}", new { title = created.Title, author = created.Author, totalCopies = 5 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var events = await GetAuditEventsAsync(created.Id);
        Assert.DoesNotContain(events, e => e.Action == "BookUpdated");
    }

    [Fact]
    public async Task UpdateBook_with_insufficient_copies_does_not_leave_an_orphan_event()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlAsync($"UPDATE books SET available_copies = 0 WHERE \"Id\" = {created!.Id}");
        }

        var response = await _client.PatchAsJsonAsync($"/books/{created!.Id}", new { title = created.Title, author = created.Author, totalCopies = 1 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var events = await GetAuditEventsAsync(created.Id);
        Assert.DoesNotContain(events, e => e.Action == "BookUpdated");
    }

    [Fact]
    public async Task UpdateBook_on_nonexistent_book_does_not_write_an_event()
    {
        var nonexistentId = Guid.NewGuid();

        var response = await _client.PatchAsJsonAsync($"/books/{nonexistentId}", new { title = "Title", author = "Author", totalCopies = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var events = await GetAuditEventsAsync(nonexistentId);
        Assert.Empty(events);
    }

    [Fact]
    public async Task DeactivateBook_effective_deactivation_is_audited()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        await _client.DeleteAsync($"/books/{created!.Id}");

        var events = await GetAuditEventsAsync(created.Id);
        var deactivated = Assert.Single(events, e => e.Action == "BookDeactivated");
        var payload = deactivated.Payload.RootElement;
        Assert.True(payload.GetProperty("before").GetProperty("isActive").GetBoolean());
        Assert.False(payload.GetProperty("after").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task DeactivateBook_already_inactive_does_not_duplicate_the_event()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();
        await _client.DeleteAsync($"/books/{created!.Id}");

        await _client.DeleteAsync($"/books/{created.Id}");

        var events = await GetAuditEventsAsync(created.Id);
        Assert.Single(events, e => e.Action == "BookDeactivated");
    }

    [Fact]
    public async Task CreateBook_records_the_client_correlation_id_on_its_audit_event()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/books");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "books-correlation-id");
        request.Content = JsonContent.Create(ValidCreateRequest());

        var created = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<BookResponse>();

        var events = await GetAuditEventsAsync(created!.Id);
        var auditEvent = Assert.Single(events, e => e.Action == "BookCreated");
        Assert.Equal("books-correlation-id", auditEvent.CorrelationId);
    }

    [Fact]
    public async Task UpdateBook_records_the_client_correlation_id_on_its_audit_event()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/books/{created!.Id}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "books-correlation-id");
        request.Content = JsonContent.Create(new { title = "Clean Code, 2nd Edition", author = created.Author, totalCopies = created.TotalCopies });

        await _client.SendAsync(request);

        var events = await GetAuditEventsAsync(created.Id);
        var auditEvent = Assert.Single(events, e => e.Action == "BookUpdated");
        Assert.Equal("books-correlation-id", auditEvent.CorrelationId);
    }

    [Fact]
    public async Task DeactivateBook_records_the_client_correlation_id_on_its_audit_event()
    {
        var created = await (await _client.PostAsJsonAsync("/books", ValidCreateRequest())).Content.ReadFromJsonAsync<BookResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/books/{created!.Id}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "books-correlation-id");

        await _client.SendAsync(request);

        var events = await GetAuditEventsAsync(created.Id);
        var auditEvent = Assert.Single(events, e => e.Action == "BookDeactivated");
        Assert.Equal("books-correlation-id", auditEvent.CorrelationId);
    }

    [Fact]
    public async Task CreateBook_without_client_correlation_id_uses_the_generated_response_header_value()
    {
        var response = await _client.PostAsJsonAsync("/books", ValidCreateRequest());
        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        var created = await response.Content.ReadFromJsonAsync<BookResponse>();

        var events = await GetAuditEventsAsync(created!.Id);
        var auditEvent = Assert.Single(events, e => e.Action == "BookCreated");
        Assert.Equal(header, auditEvent.CorrelationId);
    }

    private async Task<List<AuditEvent>> GetAuditEventsAsync(Guid bookId)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.AuditEvents.Where(e => e.EntityId == bookId).ToListAsync();
    }
}
