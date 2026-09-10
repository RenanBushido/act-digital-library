namespace Library.UnitTests.Features.Books;

public class BookTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_with_valid_data_is_active_and_available_equals_total()
    {
        var book = Book.Create("Clean Code", "978-3-16-148410-0", "Robert C. Martin", totalCopies: 3, availableCopies: 3, Clock);

        Assert.True(book.IsActive);
        Assert.Equal(3, book.AvailableCopies);
        Assert.Equal(3, book.TotalCopies);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_throws_when_total_copies_is_not_positive(int totalCopies)
    {
        Assert.Throws<DomainException>(() =>
            Book.Create("Title", "978-3-16-148410-0", "Author", totalCopies, totalCopies, Clock));
    }

    [Fact]
    public void Create_throws_when_available_copies_is_greater_than_total_copies()
    {
        Assert.Throws<DomainException>(() =>
            Book.Create("Title", "978-3-16-148410-0", "Author", totalCopies: 2, availableCopies: 3, Clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_title_is_empty(string title)
    {
        Assert.Throws<DomainException>(() =>
            Book.Create(title, "978-3-16-148410-0", "Author", totalCopies: 1, availableCopies: 1, Clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_isbn_is_empty(string isbn)
    {
        Assert.Throws<DomainException>(() =>
            Book.Create("Title", isbn, "Author", totalCopies: 1, availableCopies: 1, Clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_author_is_empty(string author)
    {
        Assert.Throws<DomainException>(() =>
            Book.Create("Title", "978-3-16-148410-0", author, totalCopies: 1, availableCopies: 1, Clock));
    }
}
