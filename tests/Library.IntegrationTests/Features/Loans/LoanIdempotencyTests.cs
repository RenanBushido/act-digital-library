namespace Library.IntegrationTests.Features.Loans;

[Collection("Api")]
public class LoanIdempotencyTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int ConcurrentRequests = 20;

    private readonly ApiFixture _fixture = fixture;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Repeating_same_key_and_body_replays_original_response_and_does_not_reduce_availability_again()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var idempotencyKey = Guid.NewGuid().ToString();

        var first = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, userId, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstLoan = await first.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));

        var second = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, userId, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        // Comparação estrutural (não byte-a-byte): jsonb reformata espaçamento ao persistir/reler
        // (ex.: espaço após ":"), mas preserva o conteúdo semântico — o que o requisito exige.
        var secondLoan = await second.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.Equal(firstLoan, secondLoan);
        Assert.True(second.Headers.TryGetValues("Idempotency-Replayed", out var values));
        Assert.Equal("true", values!.Single());

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var loanCount = await dbContext.Loans.CountAsync(l => l.BookId == book.Id);
        Assert.Equal(1, loanCount);

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(book.AvailableCopies - 1, bookAfter!.AvailableCopies);
    }

    [Fact]
    public async Task Same_key_with_different_body_returns_422_and_keeps_original_loan_intact()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 2);
        var firstUserId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var secondUserId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var idempotencyKey = Guid.NewGuid().ToString();

        var first = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, firstUserId, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var originalLoan = await first.Content.ReadFromJsonAsync<LoanResponse>();

        var second = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, secondUserId, idempotencyKey);

        Assert.Equal((HttpStatusCode)422, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency-key-reuse", problem.GetProperty("type").GetString());

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loan = await dbContext.Loans.SingleAsync(l => l.Id == originalLoan!.Id);
        Assert.Equal(LoanStatus.Active, loan.Status);

        var loanCount = await dbContext.Loans.CountAsync(l => l.BookId == book.Id);
        Assert.Equal(1, loanCount);
    }

    [Fact]
    public async Task Request_with_key_already_in_flight_returns_409()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var idempotencyKey = Guid.NewGuid().ToString();

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(new { bookId = book.Id, userId });
        var requestHash = ComputeRequestHash(bodyBytes);

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

            var reserved = IdempotencyKey.Reserve(
                idempotencyKey,
                RequireIdempotencyKeyFilter.Endpoint,
                requestHash,
                timeProvider.GetUtcNow(),
                TimeSpan.FromHours(24));

            dbContext.IdempotencyKeys.Add(reserved);
            await dbContext.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("request-in-flight", problem.GetProperty("type").GetString());

        await using var verifyScope = _fixture.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loanCount = await verifyDbContext.Loans.CountAsync(l => l.BookId == book.Id);
        Assert.Equal(0, loanCount);
    }

    [Fact]
    public async Task Business_rejection_releases_key_and_a_later_retry_with_same_key_can_succeed()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var firstUserId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var secondUserId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var firstLoan = await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, firstUserId);

        var idempotencyKey = Guid.NewGuid().ToString();

        var rejected = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, secondUserId, idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no-copy-available", problem.GetProperty("type").GetString());

        await _client.PostAsync($"/loans/{firstLoan.Id}/return", content: null);

        var retried = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, secondUserId, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Concurrent_requests_with_same_key_produce_exactly_one_loan()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: ConcurrentRequests);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var idempotencyKey = Guid.NewGuid().ToString();

        using var barrier = new Barrier(ConcurrentRequests);

        var tasks = Enumerable.Range(0, ConcurrentRequests).Select(_ => Task.Run(async () =>
        {
            using var client = _fixture.CreateClient();
            barrier.SignalAndWait();

            return await LoanTestHelpers.PostLoanWithKeyAsync(client, book.Id, userId, idempotencyKey);
        }));

        var responses = await Task.WhenAll(tasks);

        // 201 sem o header é a criação original; 201 com o header é replay; 409 é `request-in-flight`.
        var originals = responses.Count(r => r.StatusCode == HttpStatusCode.Created && !r.Headers.Contains("Idempotency-Replayed"));
        var replays = responses.Count(r => r.StatusCode == HttpStatusCode.Created && r.Headers.Contains("Idempotency-Replayed"));
        var inFlightRejections = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, originals);
        Assert.Equal(ConcurrentRequests - 1, replays + inFlightRejections);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var loanCount = await dbContext.Loans.CountAsync(l => l.BookId == book.Id);
        Assert.Equal(1, loanCount);

        var bookAfter = await (await _client.GetAsync($"/books/{book.Id}")).Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(ConcurrentRequests - 1, bookAfter!.AvailableCopies);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static string ComputeRequestHash(byte[] bodyBytes)
    {
        using var sha256 = SHA256.Create();
        sha256.TransformBlock(bodyBytes, 0, bodyBytes.Length, null, 0);
        var routeBytes = Encoding.UTF8.GetBytes(RequireIdempotencyKeyFilter.Endpoint);
        sha256.TransformFinalBlock(routeBytes, 0, routeBytes.Length);
        return Convert.ToHexString(sha256.Hash!);
    }
}
