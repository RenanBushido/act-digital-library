namespace Library.Api.Common.Errors;

public static class ErrorHttpResultExtensions
{
    public static IResult ToProblem(this Error error) =>
        TypedResults.Problem(statusCode: error.StatusCode, title: error.Title, type: error.Type);
}
