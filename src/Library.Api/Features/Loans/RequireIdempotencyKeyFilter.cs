namespace Library.Api.Features.Loans;

public sealed class RequireIdempotencyKeyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var value = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            return ValueTask.FromResult<object?>(LoanErrors.IdempotencyKeyRequired().ToProblem());
        }

        return next(context);
    }
}
