namespace BillingCrm.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Every operation in this group is Admin-only. Later specs attach real Admin features here.
        var group = app.MapGroup("/api/admin").RequireAuthorization(AuthorizationPolicies.AdminOnly);

        // Placeholder with no business content; it exists so the role rule can be built and tested.
        group.MapGet("/status", () => Results.Ok(new { area = "admin", status = "ok" }));

        return app;
    }
}
