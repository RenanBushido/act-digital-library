namespace Library.IntegrationTests.Features.Loans;

public class LoanMetricsTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture = new();
    private HttpClient _client = null!;
    private LoanMetrics _loanMetrics = null!;

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)_fixture).InitializeAsync();
        _client = _fixture.CreateClient();
        _loanMetrics = _fixture.Services.GetRequiredService<LoanMetrics>();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_fixture).DisposeAsync();

    [Fact]
    public async Task Successful_loan_creation_increments_created_and_records_duration_with_created_outcome()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        using var capture = new MetricsCapture(_loanMetrics.Meter);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, capture.CountOf("library.loans.created"));
        Assert.Equal(1, capture.CountOf("library.loans.create.duration", tags => Equals(tags["outcome"], "created")));
    }

    [Fact]
    public async Task Rejection_for_unavailable_copy_increments_rejected_with_reason_unavailable()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_fixture.CreateClient(), totalCopies: 1);
        var firstUserId = await LoanTestHelpers.CreateUserAsync(_fixture, name: "Primeiro");
        var secondUserId = await LoanTestHelpers.CreateUserAsync(_fixture, name: "Segundo");
        await LoanTestHelpers.CreateActiveLoanAsync(_client, book.Id, firstUserId);

        using var capture = new MetricsCapture(_loanMetrics.Meter);

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, secondUserId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, capture.CountOf("library.loans.rejected", tags => Equals(tags["reason"], "unavailable")));
        Assert.Equal(1, capture.CountOf("library.loans.create.duration", tags => Equals(tags["outcome"], "rejected")));
    }

    [Fact]
    public async Task Rejection_for_inactive_book_not_found_book_and_not_found_user_increment_rejected_with_matching_reason()
    {
        var activeBook = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        await _client.DeleteAsync($"/books/{activeBook.Id}");

        using (var capture = new MetricsCapture(_loanMetrics.Meter))
        {
            var inactiveResponse = await LoanTestHelpers.PostLoanAsync(_client, activeBook.Id, userId);
            Assert.Equal(HttpStatusCode.Conflict, inactiveResponse.StatusCode);
            Assert.Equal(1, capture.CountOf("library.loans.rejected", tags => Equals(tags["reason"], "book_inactive")));
        }

        using (var capture = new MetricsCapture(_loanMetrics.Meter))
        {
            var bookNotFoundResponse = await LoanTestHelpers.PostLoanAsync(_client, Guid.NewGuid(), userId);
            Assert.Equal(HttpStatusCode.NotFound, bookNotFoundResponse.StatusCode);
            Assert.Equal(1, capture.CountOf("library.loans.rejected", tags => Equals(tags["reason"], "book_not_found")));
        }

        var otherBook = await LoanTestHelpers.CreateBookAsync(_client, totalCopies: 1, isbn: "978-0-13-468599-1");
        using (var capture = new MetricsCapture(_loanMetrics.Meter))
        {
            var userNotFoundResponse = await LoanTestHelpers.PostLoanAsync(_client, otherBook.Id, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.NotFound, userNotFoundResponse.StatusCode);
            Assert.Equal(1, capture.CountOf("library.loans.rejected", tags => Equals(tags["reason"], "user_not_found")));
        }
    }

    [Fact]
    public async Task Idempotent_replay_increments_idempotent_replays_and_does_not_increment_rejected()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);
        var idempotencyKey = Guid.NewGuid().ToString();

        var first = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, userId, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var capture = new MetricsCapture(_loanMetrics.Meter);

        var second = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, userId, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(second.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, capture.CountOf("library.loans.idempotent_replays"));
        Assert.Equal(0, capture.CountOf("library.loans.rejected"));
        Assert.Equal(1, capture.CountOf("library.loans.create.duration", tags => Equals(tags["outcome"], "replayed")));
    }

    [Fact]
    public async Task Idempotency_responses_without_a_business_reason_do_not_increment_rejected_nor_idempotent_replays()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        using (var capture = new MetricsCapture(_loanMetrics.Meter))
        {
            var missingKeyResponse = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId, includeIdempotencyKey: false);
            Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);
            Assert.Equal(0, capture.CountOf("library.loans.rejected"));
            Assert.Equal(0, capture.CountOf("library.loans.idempotent_replays"));
            Assert.Equal(1, capture.CountOf("library.loans.create.duration", tags => Equals(tags["outcome"], "rejected")));
        }

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

        using (var capture = new MetricsCapture(_loanMetrics.Meter))
        {
            var inFlightResponse = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, userId, idempotencyKey);
            Assert.Equal(HttpStatusCode.Conflict, inFlightResponse.StatusCode);
            Assert.Equal(0, capture.CountOf("library.loans.rejected"));
            Assert.Equal(0, capture.CountOf("library.loans.idempotent_replays"));
            Assert.Equal(1, capture.CountOf("library.loans.create.duration", tags => Equals(tags["outcome"], "rejected")));
        }

        var reuseKey = Guid.NewGuid().ToString();
        var otherUserId = await LoanTestHelpers.CreateUserAsync(_fixture, name: "Outro");
        var original = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, userId, reuseKey);
        Assert.Equal(HttpStatusCode.Created, original.StatusCode);

        using (var capture = new MetricsCapture(_loanMetrics.Meter))
        {
            var reuseResponse = await LoanTestHelpers.PostLoanWithKeyAsync(_client, book.Id, otherUserId, reuseKey);
            Assert.Equal((HttpStatusCode)422, reuseResponse.StatusCode);
            Assert.Equal(0, capture.CountOf("library.loans.rejected"));
            Assert.Equal(0, capture.CountOf("library.loans.idempotent_replays"));
            Assert.Equal(1, capture.CountOf("library.loans.create.duration", tags => Equals(tags["outcome"], "rejected")));
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
