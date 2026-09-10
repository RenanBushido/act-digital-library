namespace Library.Api.Features.Books;

public static class BooksEndpoints
{
    public static IEndpointRouteBuilder MapBooksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/books");

        group.MapGet("/", ListBooks.HandleAsync);
        group.MapGet("/{id:guid}", GetBook.HandleAsync);
        group.MapGet("/{id:guid}/availability", GetBookAvailability.HandleAsync);
        group.MapGet("/{id:guid}/history", GetBookHistory.HandleAsync);
        group.MapPost("/", CreateBook.HandleAsync);
        group.MapPatch("/{id:guid}", UpdateBook.HandleAsync);
        group.MapDelete("/{id:guid}", DeactivateBook.HandleAsync);

        return app;
    }
}
