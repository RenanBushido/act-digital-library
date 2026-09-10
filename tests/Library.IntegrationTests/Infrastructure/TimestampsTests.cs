namespace Library.IntegrationTests.Infrastructure;

[Collection("Postgres")]
public class TimestampsTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public TimestampsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Book_and_user_timestamps_are_persisted_as_utc_timestamptz()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var book = Book.Create("Clean Code", "978-3-16-148410-0", "Robert C. Martin", 1, 1, TimeProvider.System);
        var user = User.Create("Leitor", "leitor@example.com", TimeProvider.System);
        dbContext.Books.Add(book);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var bookColumnType = await dbContext.Database.SqlQuery<string>(
                $"SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name = 'books' AND column_name = 'created_at_utc'")
            .SingleAsync();
        var userColumnType = await dbContext.Database.SqlQuery<string>(
                $"SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'created_at_utc'")
            .SingleAsync();

        Assert.Equal("timestamp with time zone", bookColumnType);
        Assert.Equal("timestamp with time zone", userColumnType);

        await using var freshDbContext = _fixture.CreateDbContext();
        var persistedBook = await freshDbContext.Books.SingleAsync(b => b.Id == book.Id);
        var persistedUser = await freshDbContext.Users.SingleAsync(u => u.Id == user.Id);

        Assert.Equal(TimeSpan.Zero, persistedBook.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, persistedUser.CreatedAtUtc.Offset);
    }
}
