using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingCrm.Api.Tests;

/// <summary>
/// The access rule of spec 004 read straight from the endpoint metadata: reading is for any signed-in user,
/// exactly the three writes require the Admin-only policy, and no product route can delete.
/// </summary>
public class ProductAccessTests
{
    private static List<(string Route, RouteEndpoint Endpoint)> ProductRoutes(ApiFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/products", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => ($"{method} {endpoint.RoutePattern.RawText!.TrimEnd('/')}", endpoint)))
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Only_the_documented_product_operations_exist_and_none_is_a_delete()
    {
        using var factory = new ApiFactory();

        var routes = ProductRoutes(factory).Select(pair => pair.Route).ToList();

        Assert.DoesNotContain(routes, route => route.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "GET /api/products",
                "GET /api/products/{id}",
                "POST /api/products",
                "PUT /api/products/{id}",
                "PUT /api/products/{id}/active",
            },
            routes);
    }

    [Fact]
    public void Exactly_the_three_writes_require_the_admin_only_policy()
    {
        using var factory = new ApiFactory();

        var adminOnly = ProductRoutes(factory)
            .Where(pair => pair.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == "AdminOnly"))
            .Select(pair => pair.Route)
            .ToList();

        Assert.Equal(
            new[] { "POST /api/products", "PUT /api/products/{id}", "PUT /api/products/{id}/active" },
            adminOnly);
    }

    [Fact]
    public void Every_product_route_requires_a_signed_in_user_and_none_is_anonymous()
    {
        using var factory = new ApiFactory();

        foreach (var (route, endpoint) in ProductRoutes(factory))
        {
            Assert.True(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(), $"{route} declares no authorization");
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }
}
