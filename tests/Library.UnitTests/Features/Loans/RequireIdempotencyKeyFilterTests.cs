namespace Library.UnitTests.Features.Loans;

public class RequireIdempotencyKeyFilterTests
{
    private readonly RequireIdempotencyKeyFilter _filter = new();

    [Fact]
    public async Task InvokeAsync_without_header_short_circuits_with_idempotency_key_required()
    {
        var context = CreateInvocationContext();

        var result = await _filter.InvokeAsync(context, _ => throw new InvalidOperationException("next should not run"));

        var problem = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("idempotency-key-required", problem.ProblemDetails.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_with_blank_header_short_circuits(string headerValue)
    {
        var context = CreateInvocationContext(headerValue);

        var result = await _filter.InvokeAsync(context, _ => throw new InvalidOperationException("next should not run"));

        var problem = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal("idempotency-key-required", problem.ProblemDetails.Type);
    }

    [Fact]
    public async Task InvokeAsync_with_header_present_calls_next()
    {
        var context = CreateInvocationContext(Guid.NewGuid().ToString());
        var nextCalled = false;

        var result = await _filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(TypedResults.Ok());
        });

        Assert.True(nextCalled);
        Assert.IsAssignableFrom<Ok>(result);
    }

    private static EndpointFilterInvocationContext CreateInvocationContext(string? idempotencyKeyHeader = null)
    {
        var httpContext = new DefaultHttpContext();
        if (idempotencyKeyHeader is not null)
        {
            httpContext.Request.Headers[RequireIdempotencyKeyFilter.HeaderName] = idempotencyKeyHeader;
        }

        return EndpointFilterInvocationContext.Create(httpContext);
    }
}
