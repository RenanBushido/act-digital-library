namespace Library.UnitTests.Infrastructure.CachingTests;

public class BookListFilterTests
{
    [Fact]
    public void ComputeHash_is_insensitive_to_case_and_surrounding_whitespace()
    {
        var a = new BookListFilter(" Clean Code ", " Robert ", "978-3-16-148410-0", false, 1, 20);
        var b = new BookListFilter("clean code", "robert", "978-3-16-148410-0", false, 1, 20);

        Assert.Equal(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void ComputeHash_differs_for_different_title()
    {
        var a = new BookListFilter("Clean Code", null, null, false, 1, 20);
        var b = new BookListFilter("Refactoring", null, null, false, 1, 20);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void ComputeHash_differs_for_different_author()
    {
        var a = new BookListFilter(null, "Martin", null, false, 1, 20);
        var b = new BookListFilter(null, "Fowler", null, false, 1, 20);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void ComputeHash_differs_when_includeInactive_differs()
    {
        var a = new BookListFilter(null, null, null, false, 1, 20);
        var b = new BookListFilter(null, null, null, true, 1, 20);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void ComputeHash_differs_for_different_page_or_pageSize()
    {
        var basePage = new BookListFilter(null, null, null, false, 1, 20);
        var differentPage = new BookListFilter(null, null, null, false, 2, 20);
        var differentPageSize = new BookListFilter(null, null, null, false, 1, 50);

        Assert.NotEqual(basePage.ComputeHash(), differentPage.ComputeHash());
        Assert.NotEqual(basePage.ComputeHash(), differentPageSize.ComputeHash());
    }
}
