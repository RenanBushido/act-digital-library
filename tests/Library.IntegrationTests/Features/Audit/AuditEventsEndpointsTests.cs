using Library.IntegrationTests.Features.Loans;

namespace Library.IntegrationTests.Features.Audit;

[Collection("Api")]
public class AuditEventsEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object ValidCreateBookRequest(string isbn = "978-3-16-148410-0", string title = "Clean Code") =>
        new { title, isbn, author = "Robert C. Martin", totalCopies = 3 };

    [Fact]
    public async Task ListAuditEvents_returns_200_with_paginated_events()
    {
        await _client.PostAsJsonAsync("/books", ValidCreateBookRequest());

        var response = await _client.GetAsync("/audit-events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Items);
    }

    [Fact]
    public async Task Filter_by_entity_type_and_entity_id_returns_only_that_entitys_events()
    {
        var book = await (await _client.PostAsJsonAsync("/books", ValidCreateBookRequest())).Content.ReadFromJsonAsync<BookResponse>();
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        await LoanTestHelpers.CreateActiveLoanAsync(_client, book!.Id, userId);

        var response = await _client.GetAsync($"/audit-events?entityType=Book&entityId={book.Id}");
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        Assert.All(body!.Items, e => Assert.Equal("Book", e.EntityType));
        Assert.All(body.Items, e => Assert.Equal(book.Id, e.EntityId));
    }

    [Fact]
    public async Task Filter_by_action_returns_only_matching_events()
    {
        var book = await (await _client.PostAsJsonAsync("/books", ValidCreateBookRequest())).Content.ReadFromJsonAsync<BookResponse>();
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        await LoanTestHelpers.CreateActiveLoanAsync(_client, book!.Id, userId);

        var response = await _client.GetAsync("/audit-events?action=BookCreated");
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        Assert.NotEmpty(body!.Items);
        Assert.All(body.Items, e => Assert.Equal("BookCreated", e.Action));
    }

    [Fact]
    public async Task Filter_by_actor_returns_only_matching_events()
    {
        var book = await (await _client.PostAsJsonAsync("/books", ValidCreateBookRequest())).Content.ReadFromJsonAsync<BookResponse>();
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/books/{book!.Id}");
        deleteRequest.Headers.Add("X-Actor", "librarian");
        await _client.SendAsync(deleteRequest);

        var response = await _client.GetAsync("/audit-events?actor=librarian");
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        var auditEvent = Assert.Single(body!.Items);
        Assert.Equal("BookDeactivated", auditEvent.Action);
        Assert.Equal("librarian", auditEvent.Actor);
    }

    [Fact]
    public async Task Filter_by_correlation_id_returns_exactly_the_events_of_that_request()
    {
        var book = await (await _client.PostAsJsonAsync("/books", ValidCreateBookRequest())).Content.ReadFromJsonAsync<BookResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/books/{book!.Id}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "audit-filter-correlation-id");
        await _client.SendAsync(request);

        var response = await _client.GetAsync("/audit-events?correlationId=audit-filter-correlation-id");
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        var auditEvent = Assert.Single(body!.Items);
        Assert.Equal("BookDeactivated", auditEvent.Action);
        Assert.Equal(book.Id, auditEvent.EntityId);
        Assert.Equal("audit-filter-correlation-id", auditEvent.CorrelationId);
    }

    [Fact]
    public async Task Filter_by_date_range_returns_only_events_within_range()
    {
        var insideRange = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var outsideRange = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var insideId = Guid.NewGuid();
        var outsideId = Guid.NewGuid();

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var payloadInside = JsonSerializer.SerializeToDocument(new { after = new { title = "Inside" } });
            using var payloadOutside = JsonSerializer.SerializeToDocument(new { after = new { title = "Outside" } });

            dbContext.AuditEvents.Add(AuditEvent.Create("Book", insideId, "BookCreated", "anonymous", insideRange, "corr-inside", payloadInside));
            dbContext.AuditEvents.Add(AuditEvent.Create("Book", outsideId, "BookCreated", "anonymous", outsideRange, "corr-outside", payloadOutside));
            await dbContext.SaveChangesAsync();
        }

        var from = Uri.EscapeDataString(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("O"));
        var to = Uri.EscapeDataString(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero).ToString("O"));

        var response = await _client.GetAsync($"/audit-events?from={from}&to={to}");
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        Assert.Contains(body!.Items, e => e.EntityId == insideId);
        Assert.DoesNotContain(body.Items, e => e.EntityId == outsideId);
    }

    [Fact]
    public async Task Filter_combination_matching_nothing_returns_an_empty_page_not_an_error()
    {
        await _client.PostAsJsonAsync("/books", ValidCreateBookRequest());

        var response = await _client.GetAsync("/audit-events?action=BookCreated&actor=someone-who-never-acted");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task Pagination_with_two_events_at_the_same_instant_does_not_skip_or_repeat()
    {
        var sameInstant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var payloadA = JsonSerializer.SerializeToDocument(new { after = new { title = "A" } });
            using var payloadB = JsonSerializer.SerializeToDocument(new { after = new { title = "B" } });

            dbContext.AuditEvents.Add(AuditEvent.Create("Book", Guid.NewGuid(), "BookCreated", "anonymous", sameInstant, "corr-a", payloadA));
            dbContext.AuditEvents.Add(AuditEvent.Create("Book", Guid.NewGuid(), "BookCreated", "anonymous", sameInstant, "corr-b", payloadB));
            await dbContext.SaveChangesAsync();
        }

        var firstPage = await (await _client.GetAsync("/audit-events?page=1&pageSize=1")).Content.ReadFromJsonAsync<PagedAuditEventResponse>();
        var secondPage = await (await _client.GetAsync("/audit-events?page=2&pageSize=1")).Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        var firstId = Assert.Single(firstPage!.Items).Id;
        var secondId = Assert.Single(secondPage!.Items).Id;
        Assert.NotEqual(firstId, secondId);

        // Repetir a mesma consulta produz a mesma ordem — a paginação é determinística, não apenas "sem duplicar por acaso".
        var firstPageAgain = await (await _client.GetAsync("/audit-events?page=1&pageSize=1")).Content.ReadFromJsonAsync<PagedAuditEventResponse>();
        Assert.Equal(firstId, Assert.Single(firstPageAgain!.Items).Id);
    }

    [Fact]
    public async Task No_write_endpoint_exists_for_audit_events()
    {
        var postResponse = await _client.PostAsync("/audit-events", content: null);
        var deleteResponse = await _client.DeleteAsync("/audit-events/1");

        // `/audit-events` só mapeia GET: POST bate no grupo mas com verbo não suportado (405);
        // não existe rota de item (`/audit-events/{id}`) alguma, então DELETE não resolve rota (404).
        Assert.Equal(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task GetAuditEvents_without_client_correlation_id_still_echoes_a_generated_one()
    {
        var response = await _client.GetAsync("/audit-events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(header));
    }
}
