namespace Library.UnitTests.Domain.BookTests;

public class IsbnTests
{
    [Fact]
    public void Create_normalizes_hyphens_and_case_to_the_same_value()
    {
        var withHyphens = Isbn.Create("978-3-16-148410-0");
        var withoutHyphens = Isbn.Create("9783161484100");

        Assert.Equal(withHyphens.Value, withoutHyphens.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    public void Create_throws_when_value_is_empty_after_normalization(string rawValue)
    {
        Assert.Throws<DomainException>(() => Isbn.Create(rawValue));
    }
}
