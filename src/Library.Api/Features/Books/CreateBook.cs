namespace Library.Api.Features.Books;

public sealed record CreateBookRequest(
    [property: Required, MinLength(1)] string Title,
    [property: Required, MinLength(1)] string Isbn,
    [property: Required, MinLength(1)] string Author,
    [property: Range(1, int.MaxValue)] int TotalCopies);

public static class CreateBook
{
    public static async Task<IResult> HandleAsync(
        CreateBookRequest request,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IDistributedCache cache,
        ILogger<BooksLog> logger,
        CancellationToken cancellationToken)
    {
        Book book;
        try
        {
            book = Book.Create(request.Title, request.Isbn, request.Author, request.TotalCopies, request.TotalCopies, timeProvider);
        }
        catch (DomainException ex)
        {
            return BookErrors.ValidationFailed(ex.Message).ToProblem();
        }

        dbContext.Books.Add(book);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return BookErrors.IsbnDuplicate(request.Isbn).ToProblem();
        }

        // Só a chave da página/tamanho padrão é invalidada; páginas não padrão expiram pelo TTL.
        await CacheReadThrough.RemoveAsync(
            cache,
            logger,
            ListBooks.CacheKey(ListBooks.DefaultPage, ListBooks.DefaultPageSize),
            cancellationToken);

        return TypedResults.Created($"/books/{book.Id}", BookResponse.From(book));
    }
}
