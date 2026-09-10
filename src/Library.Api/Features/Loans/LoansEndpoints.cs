namespace Library.Api.Features.Loans;

public static class LoansEndpoints
{
    public static IEndpointRouteBuilder MapLoansEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/loans");

        group.MapPost("/", CreateLoan.HandleAsync).AddEndpointFilter<RequireIdempotencyKeyFilter>();
        group.MapPost("/{id:guid}/return", ReturnLoan.HandleAsync);
        group.MapPost("/{id:guid}/cancel", CancelLoan.HandleAsync);

        return app;
    }
}
