namespace Library.Api.Extensions;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string
                    ?? context.HttpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();

                context.ProblemDetails.Extensions["correlationId"] = correlationId;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });

        return services;
    }
}
