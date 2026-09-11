namespace Library.IntegrationTests.Features.Loans;

[Collection("Api")]
public class LoansEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateLoan_with_valid_body_returns_201_and_decrements_available_copies()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.NotNull(loan);
        Assert.Equal("Active", loan!.Status);
        Assert.Equal(loan.LoanedAtUtc.AddDays(14), loan.DueAtUtc);
        Assert.Equal($"/loans/{loan.Id}", response.Headers.Location?.OriginalString);

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(0, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task CreateLoan_with_empty_book_id_returns_400_validation_failed_without_creating_loan()
    {
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var response = await LoanTestHelpers.PostLoanAsync(_client, Guid.Empty, userId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation-failed", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateLoan_with_empty_user_id_returns_400_validation_failed()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, Guid.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation-failed", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateLoan_without_idempotency_key_returns_400()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId, includeIdempotencyKey: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency-key-required", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateLoan_with_nonexistent_book_returns_404_without_changing_any_book()
    {
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var response = await LoanTestHelpers.PostLoanAsync(_client, Guid.NewGuid(), userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateLoan_with_nonexistent_user_returns_404_without_creating_loan()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("user-not-found", problem.GetProperty("type").GetString());

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(book.AvailableCopies, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task CreateLoan_with_inactive_book_returns_409_without_consuming_copy_or_creating_loan()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        await _client.DeleteAsync($"/books/{book.Id}");

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-inactive", problem.GetProperty("type").GetString());

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(book.AvailableCopies, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task CreateLoan_without_available_copy_returns_409_without_consuming_copy_or_creating_loan()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var firstUserId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var secondUserId = await LoanTestHelpers.CreateUserAsync(_fixture);
        await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, firstUserId);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, secondUserId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no-copy-available", problem.GetProperty("type").GetString());

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(0, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task CreateLoan_same_user_can_have_two_active_loans_of_same_book()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 2);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ReturnLoan_active_loan_succeeds_and_increments_available_copies()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        var response = await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.Equal("Returned", updated!.Status);
        Assert.NotNull(updated.ReturnedAtUtc);

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(book.AvailableCopies, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task ReturnLoan_works_even_if_book_was_deactivated()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.DeleteAsync($"/books/{book.Id}");

        var response = await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(1, bookAfter!.AvailableCopies);
        Assert.False(bookAfter.IsActive);
    }

    [Fact]
    public async Task ReturnLoan_already_returned_returns_409_without_changing_available_copies()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        var response = await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loan-not-active", problem.GetProperty("type").GetString());

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(book.AvailableCopies, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task ReturnLoan_cancelled_loan_returns_409()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        var response = await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loan-not-active", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ReturnLoan_nonexistent_returns_404()
    {
        var response = await _client.PostAsync($"/loans/{Guid.NewGuid()}/return", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loan-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CancelLoan_active_loan_succeeds_preserves_record_and_increments_available_copies()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        var response = await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.Equal("Cancelled", updated!.Status);
        Assert.NotNull(updated.CancelledAtUtc);

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(book.AvailableCopies, bookAfter!.AvailableCopies);

        var history = await (await _client.GetAsync($"/books/{book.Id}/history")).Content.ReadFromJsonAsync<PagedLoanResponse>();
        Assert.Contains(history!.Items, l => l.Id == loan.Id && l.Status == "Cancelled");
    }

    [Fact]
    public async Task CancelLoan_works_even_if_book_was_deactivated()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.DeleteAsync($"/books/{book.Id}");

        var response = await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(1, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task CancelLoan_already_cancelled_returns_409_and_preserves_record()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        var response = await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loan-not-active", problem.GetProperty("type").GetString());

        var history = await (await _client.GetAsync($"/books/{book.Id}/history")).Content.ReadFromJsonAsync<PagedLoanResponse>();
        Assert.Contains(history!.Items, l => l.Id == loan.Id);
    }

    [Fact]
    public async Task CancelLoan_returned_loan_returns_409()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        await _client.PostAsync($"/loans/{loan.Id}/return", content: null);

        var response = await _client.PostAsync($"/loans/{loan.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loan-not-active", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CancelLoan_nonexistent_returns_404()
    {
        var response = await _client.PostAsync($"/loans/{Guid.NewGuid()}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loan-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Successful_loan_creation_is_audited()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        var loan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        var events = await GetAuditEventsAsync(loan.Id);
        Assert.Contains(events, e => e.Action == "LoanCreated" && e.Actor == "anonymous");
    }

    [Fact]
    public async Task CreateLoan_writes_are_tagged_with_the_requests_correlation_id()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
        {
            Content = JsonContent.Create(new { bookId = book.Id, userId }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "loan-correlation-id");

        var response = await _client.SendAsync(request);
        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();

        // O decremento de available_copies (ExecuteUpdateAsync) e a criação do Loan/AuditEvent
        // são escritas distintas da mesma requisição; o evento é o único lugar onde o
        // correlation_id fica gravado, então ele é o comprovante de que ambas compartilham o mesmo id.
        Assert.Equal("loan-correlation-id", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var events = await GetAuditEventsAsync(loan!.Id);
        var auditEvent = Assert.Single(events, e => e.Action == "LoanCreated");
        Assert.Equal("loan-correlation-id", auditEvent.CorrelationId);
    }

    [Fact]
    public async Task Successful_return_and_cancellation_are_audited()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 2);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var returned = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        var cancelled = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        using var returnRequest = new HttpRequestMessage(HttpMethod.Post, $"/loans/{returned.Id}/return");
        returnRequest.Headers.Add("X-Actor", "librarian");
        await _client.SendAsync(returnRequest);

        await _client.PostAsync($"/loans/{cancelled.Id}/cancel", content: null);

        var returnedEvents = await GetAuditEventsAsync(returned.Id);
        Assert.Contains(returnedEvents, e => e.Action == "LoanReturned" && e.Actor == "librarian");

        var cancelledEvents = await GetAuditEventsAsync(cancelled.Id);
        Assert.Contains(cancelledEvents, e => e.Action == "LoanCancelled");
    }

    [Fact]
    public async Task Loan_lifecycle_events_are_still_recorded_and_readable_via_the_audit_query_after_the_id_migration()
    {
        // Regressão da change add-loan-concurrency: audit_events.Id migrou de Guid para bigint
        // identity nesta change (add-domain-audit); os três eventos de empréstimo continuam
        // sendo gravados e devem continuar legíveis via GET /audit-events com o novo tipo de Id.
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 2);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var returned = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);
        var cancelled = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, userId);

        await _client.PostAsync($"/loans/{returned.Id}/return", content: null);
        await _client.PostAsync($"/loans/{cancelled.Id}/cancel", content: null);

        var response = await _client.GetAsync("/audit-events?entityType=Loan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedAuditEventResponse>();

        Assert.Contains(body!.Items, e => e.Action == "LoanCreated" && e.EntityId == returned.Id);
        Assert.Contains(body.Items, e => e.Action == "LoanCreated" && e.EntityId == cancelled.Id);
        Assert.Contains(body.Items, e => e.Action == "LoanReturned" && e.EntityId == returned.Id);
        Assert.Contains(body.Items, e => e.Action == "LoanCancelled" && e.EntityId == cancelled.Id);
        Assert.All(body.Items, e => Assert.True(long.Parse(e.Id) > 0));
    }

    [Fact]
    public async Task Rejected_loan_creation_is_not_audited()
    {
        var response = await LoanTestHelpers.PostLoanAsync(_client, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await dbContext.AuditEvents.CountAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CreateLoan_error_response_still_carries_correlationId_in_problem_details_with_observability_stack_registered()
    {
        // Regressão da change add-observability: a spec observability não reimplementa o
        // CorrelationIdMiddleware nem o requisito de audit-trail que já o fixa - este teste prova
        // que registrar health checks, métricas e tracing não quebra a propagação do
        // correlationId no Problem Details de um erro de POST /loans.
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
        {
            Content = JsonContent.Create(new { bookId = Guid.NewGuid(), userId }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "loans-error-correlation-id");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("book-not-found", problem.GetProperty("type").GetString());
        Assert.Equal("loans-error-correlation-id", problem.GetProperty("correlationId").GetString());
    }

    private async Task<List<AuditEvent>> GetAuditEventsAsync(Guid loanId)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.AuditEvents.Where(e => e.EntityId == loanId).ToListAsync();
    }
}
