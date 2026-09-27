using System.Security.Claims;
using BillingCrm.Application.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace BillingCrm.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Anonymous by necessity: this is how a person becomes signed in. Rate-limited (spec 012, research R5): the
        // one place an unauthenticated request can be tried repeatedly against a real account.
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");

        // Any signed-in user, Admin or Agent.
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> LoginAsync(SignInRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.SignInAsync(request, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static IResult Me(ClaimsPrincipal user) =>
        Results.Ok(new { email = user.FindFirstValue("email"), role = user.FindFirstValue("role") });
}
