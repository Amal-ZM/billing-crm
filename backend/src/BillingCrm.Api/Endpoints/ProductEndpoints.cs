using System.Security.Claims;
using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Domain;

namespace BillingCrm.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        // Reading is open to any signed-in staff member (Admin or Agent). Every change adds the AdminOnly policy on
        // top, so an Agent gets 403 before the request body is even read. There is no delete operation, by design.
        var group = app.MapGroup("/api/products").RequireAuthorization();

        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/", SearchAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPut("/{id}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);

        // Retire ("deactivate to hide") or restore. This, not a delete, is how a product leaves everyday use.
        group.MapPut("/{id}/active", SetActiveAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);

        return app;
    }

    private static async Task<IResult> CreateAsync(ProductInput input, ProductService products, CancellationToken cancellationToken)
    {
        var result = await products.CreateAsync(input, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/products/{result.Value.Id}", result.Value)
            : result.ToProblem();
    }

    // Query values arrive as text so a bad one becomes a named field error instead of a framework failure.
    private static async Task<IResult> SearchAsync(
        string? q,
        string? unit,
        string? status,
        string? page,
        string? pageSize,
        ClaimsPrincipal user,
        ProductService products,
        CancellationToken cancellationToken)
    {
        var result = await products.SearchAsync(new ProductQuery(q, unit, status, page, pageSize), CanSeeInactive(user), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> GetAsync(string id, ClaimsPrincipal user, ProductService products, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var productId))
        {
            return NotFound();
        }

        var result = await products.GetAsync(productId, CanSeeInactive(user), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(string id, ProductInput input, ProductService products, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var productId))
        {
            return NotFound();
        }

        var result = await products.UpdateAsync(productId, input, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> SetActiveAsync(string id, ProductActiveRequest request, ProductService products, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var productId))
        {
            return NotFound();
        }

        var result = await products.SetActiveAsync(productId, request, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Only an Admin may see deactivated products. The service only ever receives this yes/no, never claims.</summary>
    private static bool CanSeeInactive(ClaimsPrincipal user) => user.IsInRole(nameof(StaffRole.Admin));

    /// <summary>A value that is not even a GUID cannot be a product: same answer as an unknown id.</summary>
    private static bool TryParseId(string id, out Guid productId) => Guid.TryParse(id, out productId);

    private static IResult NotFound() => Result.Fail(ProductService.NotFound).ToProblem();
}
