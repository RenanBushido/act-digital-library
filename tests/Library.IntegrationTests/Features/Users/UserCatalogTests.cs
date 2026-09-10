namespace Library.IntegrationTests.Features.Users;

[Collection("Postgres")]
public class UserCatalogTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public UserCatalogTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Inserting_user_with_same_email_violates_uniqueness()
    {
        await using var dbContext = _fixture.CreateDbContext();

        dbContext.Users.Add(User.Create("Leitor", "leitor@example.com", TimeProvider.System));
        await dbContext.SaveChangesAsync();

        await using var secondDbContext = _fixture.CreateDbContext();
        secondDbContext.Users.Add(User.Create("Outro Leitor", "leitor@example.com", TimeProvider.System));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondDbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Inserting_user_with_email_differing_only_by_case_violates_uniqueness()
    {
        await using var dbContext = _fixture.CreateDbContext();

        dbContext.Users.Add(User.Create("Leitor", "Leitor@Example.com", TimeProvider.System));
        await dbContext.SaveChangesAsync();

        await using var secondDbContext = _fixture.CreateDbContext();
        secondDbContext.Users.Add(User.Create("Outro Leitor", "leitor@example.com", TimeProvider.System));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondDbContext.SaveChangesAsync());
    }
}
