namespace Library.IntegrationTests.Features.Books;

[Collection("Postgres")]
public class BookCatalogTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public BookCatalogTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Inserting_book_with_isbn_equivalent_after_normalization_violates_uniqueness()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var first = Book.Create("Clean Code", "978-3-16-148410-0", "Robert C. Martin", 1, 1, TimeProvider.System);
        dbContext.Books.Add(first);
        await dbContext.SaveChangesAsync();

        await using var secondDbContext = _fixture.CreateDbContext();
        var second = Book.Create("Clean Code (reprint)", "9783161484100", "Robert C. Martin", 1, 1, TimeProvider.System);
        secondDbContext.Books.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => secondDbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Inserting_books_with_distinct_isbns_succeeds()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var first = Book.Create("Clean Code", "978-3-16-148410-0", "Robert C. Martin", 1, 1, TimeProvider.System);
        var second = Book.Create("The Pragmatic Programmer", "978-0-13-595705-9", "David Thomas", 1, 1, TimeProvider.System);
        dbContext.Books.Add(first);
        dbContext.Books.Add(second);

        await dbContext.SaveChangesAsync();

        Assert.Equal(2, await dbContext.Books.CountAsync());
    }

    [Fact]
    public async Task Direct_write_violating_available_copies_bounds_is_rejected_by_database()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var book = Book.Create("Clean Code", "978-3-16-148410-0", "Robert C. Martin", totalCopies: 3, availableCopies: 3, TimeProvider.System);
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            dbContext.Database.ExecuteSqlAsync(
                $"UPDATE books SET available_copies = -1 WHERE \"Id\" = {book.Id}"));

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            dbContext.Database.ExecuteSqlAsync(
                $"UPDATE books SET available_copies = 4 WHERE \"Id\" = {book.Id}"));
    }
}
