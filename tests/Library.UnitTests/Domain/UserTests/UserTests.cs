namespace Library.UnitTests.Domain.UserTests;

public class UserTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_with_valid_data_succeeds()
    {
        var user = User.Create("Leitor", "leitor@example.com", Clock);

        Assert.Equal("Leitor", user.Name);
        Assert.Equal("leitor@example.com", user.Email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_name_is_empty(string name)
    {
        Assert.Throws<DomainException>(() => User.Create(name, "leitor@example.com", Clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_email_is_empty(string email)
    {
        Assert.Throws<DomainException>(() => User.Create("Leitor", email, Clock));
    }
}
