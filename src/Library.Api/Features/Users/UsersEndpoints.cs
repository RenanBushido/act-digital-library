namespace Library.Api.Features.Users;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users");

        group.MapGet("/{id:guid}/loans", GetUserLoans.HandleAsync);

        return app;
    }
}
