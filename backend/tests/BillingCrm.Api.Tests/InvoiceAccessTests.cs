using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingCrm.Api.Tests;

/// <summary>
/// The access rules of specs 005 and 007 read straight from the endpoint metadata: exactly the documented invoice operations exist,
/// none can delete or reopen, all are for a signed-in user, and only <c>cancel</c> additionally needs the Admin policy.
/// </summary>
public class InvoiceAccessTests
{
    private const string CancelRoute = "POST /api/invoices/{id}/cancel";

    private static List<(string Route, RouteEndpoint Endpoint)> InvoiceRoutes(ApiFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/invoices", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => ($"{method} {endpoint.RoutePattern.RawText!.TrimEnd('/')}", endpoint)))
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Only_the_documented_invoice_operations_exist_and_none_is_a_delete_or_a_reopen()
    {
        using var factory = new ApiFactory();

        var routes = InvoiceRoutes(factory).Select(pair => pair.Route).ToList();

        Assert.DoesNotContain(routes, route => route.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route.StartsWith("PATCH", StringComparison.Ordinal));
        // Payments are permanent (spec 008): the only payment route creates one; nothing reads, edits or removes a single payment.
        Assert.Equal(["POST /api/invoices/{id}/payments"], routes.Where(route => route.Contains("payment", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(routes, route => route.Contains("{paymentId}", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("status", StringComparison.OrdinalIgnoreCase)
            || route.Contains("reopen", StringComparison.OrdinalIgnoreCase)
            || route.Contains("unsend", StringComparison.OrdinalIgnoreCase));
        // History is permanent (spec 010): the only history route reads one invoice's history; nothing writes, edits or removes an entry.
        Assert.Equal(["GET /api/invoices/{id}/history"], routes.Where(route => route.Contains("history", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(routes, route => route.Contains("{entryId}", StringComparison.OrdinalIgnoreCase) || route.Contains("{historyId}", StringComparison.OrdinalIgnoreCase));
        // The dashboard's summary (spec 011) is the only other new route: a read with no id and no paging (its filters on the plain
        // GET /api/invoices route are query values, not route segments, so they never appear in this route-text list).
        Assert.Equal(["GET /api/invoices/summary"], routes.Where(route => route.Contains("summary", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            new[]
            {
                "GET /api/invoices",
                "GET /api/invoices/summary",
                "GET /api/invoices/{id}",
                "GET /api/invoices/{id}/history",
                "POST /api/invoices",
                CancelRoute,
                "POST /api/invoices/{id}/payments",
                "POST /api/invoices/{id}/send",
                "PUT /api/invoices/{id}",
            },
            routes);
    }

    [Fact]
    public void Every_invoice_route_requires_a_signed_in_user_and_none_is_anonymous()
    {
        using var factory = new ApiFactory();

        foreach (var (route, endpoint) in InvoiceRoutes(factory))
        {
            var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.True(authorization.Any(), $"{route} declares no authorization");
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    [Fact]
    public void Only_cancel_needs_the_Admin_policy_and_every_other_invoice_route_is_open_to_any_signed_in_user()
    {
        using var factory = new ApiFactory();

        foreach (var (route, endpoint) in InvoiceRoutes(factory))
        {
            var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var needsAdmin = authorization.Any(data => data.Policy == "AdminOnly");

            Assert.Equal(route == CancelRoute, needsAdmin);
            Assert.DoesNotContain(authorization, data => !string.IsNullOrEmpty(data.Roles));
        }
    }
}
