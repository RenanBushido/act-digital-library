namespace Library.UnitTests.Features.Users;

public class EmailTests
{
    [Fact]
    public void Create_normalizes_case_to_the_same_value()
    {
        var upper = Email.Create("Leitor@Example.com");
        var lower = Email.Create("leitor@example.com");

        Assert.Equal(upper.Value, lower.Value);
    }

    [Fact]
    public void Create_normalizes_surrounding_whitespace_to_the_same_value()
    {
        var withSpaces = Email.Create(" leitor@example.com ");
        var withoutSpaces = Email.Create("leitor@example.com");

        Assert.Equal(withoutSpaces.Value, withSpaces.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_value_is_empty_after_normalization(string rawValue)
    {
        Assert.Throws<DomainException>(() => Email.Create(rawValue));
    }
}
