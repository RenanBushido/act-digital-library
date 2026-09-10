namespace Library.Api.Common.Errors;

public static class LoanErrors
{
    public static Error NotFound(Guid id) =>
        new("loan-not-found", StatusCodes.Status404NotFound, $"Loan '{id}' was not found.");

    public static Error NotActive(Guid id) =>
        new("loan-not-active", StatusCodes.Status409Conflict, $"Loan '{id}' is not active.");

    public static Error NoCopyAvailable(Guid bookId) =>
        new("no-copy-available", StatusCodes.Status409Conflict, $"Book '{bookId}' has no copy available.");

    public static Error BookInactive(Guid bookId) =>
        new("book-inactive", StatusCodes.Status409Conflict, $"Book '{bookId}' is inactive.");

    public static Error BookNotFound(Guid bookId) =>
        new("book-not-found", StatusCodes.Status404NotFound, $"Book '{bookId}' was not found.");

    public static Error UserNotFound(Guid userId) =>
        new("user-not-found", StatusCodes.Status404NotFound, $"User '{userId}' was not found.");

    public static Error IdempotencyKeyRequired() =>
        new("idempotency-key-required", StatusCodes.Status400BadRequest, "The 'Idempotency-Key' header is required.");

    public static Error ValidationFailed(string detail) =>
        new("validation-failed", StatusCodes.Status400BadRequest, detail);
}
