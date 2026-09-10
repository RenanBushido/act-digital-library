namespace Library.UnitTests.Common;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Echoes_client_provided_correlation_id()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "client-correlation-id";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, NullLogger<CorrelationIdMiddleware>.Instance);

        Assert.Equal("client-correlation-id", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.Equal("client-correlation-id", context.Items[CorrelationIdMiddleware.ItemKey]);
    }

    [Fact]
    public async Task Generates_correlation_id_when_absent()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, NullLogger<CorrelationIdMiddleware>.Instance);

        var header = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.False(string.IsNullOrWhiteSpace(header));
        Assert.Equal(header, context.Items[CorrelationIdMiddleware.ItemKey]);
    }
}
