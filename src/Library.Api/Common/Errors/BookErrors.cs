namespace Library.Api.Common.Errors;

public static class BookErrors
{
    public static Error NotFound(Guid id) =>
        new("book-not-found", StatusCodes.Status404NotFound, $"Book '{id}' was not found.");

    public static Error Inactive(Guid id) =>
        new("book-inactive", StatusCodes.Status409Conflict, $"Book '{id}' is inactive.");

    public static Error IsbnDuplicate(string isbn) =>
        new("book-isbn-duplicate", StatusCodes.Status409Conflict, $"A book with ISBN '{isbn}' already exists.");

    public static Error InsufficientAvailableCopies(Guid id) =>
        new("insufficient-available-copies", StatusCodes.Status409Conflict, $"Reducing total copies for book '{id}' would make available copies negative.");

    public static Error ValidationFailed(string detail) =>
        new("validation-failed", StatusCodes.Status400BadRequest, detail);
}
