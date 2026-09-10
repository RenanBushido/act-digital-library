namespace Library.IntegrationTests.Features.Loans;

internal static class LoanTestHelpers
{
    public static async Task<BookResponse> CreateBookAsync(HttpClient client, int totalCopies = 1, string isbn = "978-3-16-148410-0")
    {
        var response = await client.PostAsJsonAsync("/books", new { title = "Clean Code", isbn, author = "Robert C. Martin", totalCopies });
        return (await response.Content.ReadFromJsonAsync<BookResponse>())!;
    }

    public static async Task<Guid> CreateUserAsync(ApiFixture fixture, string name = "Leitor", string? email = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = User.Create(name, email ?? $"{Guid.NewGuid()}@example.com", TimeProvider.System);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user.Id;
    }

    public static async Task<HttpResponseMessage> PostLoanAsync(HttpClient client, Guid bookId, Guid userId, bool includeIdempotencyKey = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
        {
            Content = JsonContent.Create(new { bookId, userId }),
        };

        if (includeIdempotencyKey)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        return await client.SendAsync(request);
    }

    public static async Task<LoanResponse> CreateActiveLoanAsync(HttpClient client, Guid bookId, Guid userId)
    {
        var response = await PostLoanAsync(client, bookId, userId);
        return (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
    }
}
