using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Spec 012, User Story 1: one consolidated proof, across customers, products and invoices at once, that every write route needs a real
/// session and that the three Agent-restricted actions are Admin only — instead of trusting that each resource's own, separately-written
/// tests never had a gap (customers, in particular, had no such exhaustive check of their own before this).
/// </summary>
public class SecurityRegressionTests
{
    private static readonly string[] ResourcePrefixes = ["/api/customers", "/api/products", "/api/invoices"];

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>Every route under the three resources, any method other than GET (a "write" in this spec's own wording).</summary>
    private static List<(string Route, RouteEndpoint Endpoint)> WriteRoutes(ApiFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => ResourcePrefixes.Any(prefix => endpoint.RoutePattern.RawText?.StartsWith(prefix, StringComparison.Ordinal) == true))
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Where(method => method != "GET")
                .Select(method => ($"{method} {endpoint.RoutePattern.RawText!.TrimEnd('/')}", endpoint)))
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .ToList();

    // ---- exhaustive, metadata-level proof (FR-001, FR-003) ----

    [Fact]
    public void Every_write_route_across_all_three_resources_requires_a_signed_in_user_and_none_is_anonymous()
    {
        using var factory = new ApiFactory();

        var routes = WriteRoutes(factory);
        Assert.True(routes.Count >= 8, "expected at least the documented write routes across customers, products and invoices");

        foreach (var (route, endpoint) in routes)
        {
            Assert.True(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(), $"{route} declares no authorization");
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    [Fact]
    public void No_delete_route_exists_for_a_customer_a_product_or_an_invoice_of_any_status()
    {
        using var factory = new ApiFactory();

        Assert.DoesNotContain(WriteRoutes(factory).Select(pair => pair.Route), route => route.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_cancel_needs_the_admin_only_policy_among_products_and_invoices_writes()
    {
        using var factory = new ApiFactory();

        var adminOnly = WriteRoutes(factory)
            .Where(pair => pair.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == "AdminOnly"))
            .Select(pair => pair.Route)
            .ToList();

        Assert.Equal(
            new[] { "POST /api/invoices/{id}/cancel", "POST /api/products", "PUT /api/products/{id}", "PUT /api/products/{id}/active" },
            adminOnly.OrderBy(route => route, StringComparer.Ordinal));
    }

    // ---- the request's own worked example, and a representative literal HTTP proof per resource (FR-001) ----

    [Fact]
    public async Task Given_no_token_when_I_POST_api_invoices_then_401_and_nothing_is_created()
    {
        var factory = new ApiFactory();
        await using var factoryScope = factory;
        var customer = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", DateTime.UtcNow);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, DateTime.UtcNow);
        factory.ProductStore.Seed(product);
        using var client = factory.CreateClient();
        var body = $"{{\"customerId\":\"{customer.Id}\",\"dueDate\":\"{DateTime.UtcNow.AddDays(30):yyyy-MM-dd}\",\"lines\":[{{\"productId\":\"{product.Id}\",\"quantity\":1}}]}}";

        var response = await client.PostAsync("/api/invoices", Json(body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("/api/customers", "{}")]
    [InlineData("/api/products", "{}")]
    [InlineData("/api/invoices", "{}")]
    public async Task Every_resources_create_route_with_no_token_is_401(string route, string body)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(route, Json(body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_and_a_tampered_token_are_both_401_on_a_write_route_same_as_no_token()
    {
        using var factory = new ApiFactory();
        using var expired = factory.CreateClient();
        expired.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Expired());
        using var tampered = factory.CreateClient();
        tampered.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Tampered());

        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.PostAsync("/api/invoices", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tampered.PostAsync("/api/invoices", Json("{}"))).StatusCode);
    }

    // ---- an Agent on the three restricted actions: 403 before the body is read, nothing changes; an Admin succeeds (FR-002) ----

    [Fact]
    public async Task Agent_is_403_on_the_three_restricted_actions_even_with_a_malformed_body_and_nothing_changes()
    {
        var factory = new ApiFactory();
        await using var factoryScope = factory;
        var customer = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", DateTime.UtcNow);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, DateTime.UtcNow);
        factory.ProductStore.Seed(product);
        var invoice = Invoice.Create(customer.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, DateTime.UtcNow);
        invoice.AddLine(product, 1m, DateTime.UtcNow);
        Assert.True(invoice.Send(DateTime.UtcNow));
        await factory.InvoiceStore.AddAsync(invoice, default);
        using var agent = factory.CreateAuthorizedClient("Agent");
        var garbage = Json("not even json {");

        var createProduct = await agent.PostAsync("/api/products", garbage);
        var editProduct = await agent.PutAsync($"/api/products/{product.Id}", garbage);
        var activateProduct = await agent.PutAsync($"/api/products/{product.Id}/active", garbage);
        var cancelInvoice = await agent.PostAsync($"/api/invoices/{invoice.Id}/cancel", garbage);

        Assert.Equal(HttpStatusCode.Forbidden, createProduct.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, editProduct.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, activateProduct.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancelInvoice.StatusCode);
        Assert.True(product.IsActive);
        Assert.Equal(InvoiceStatus.Sent, factory.InvoiceStore.All.Single(i => i.Id == invoice.Id).Status);
        Assert.Single(factory.ProductStore.All);
    }

    [Fact]
    public async Task Admin_succeeds_on_the_same_three_restricted_actions_given_a_valid_body()
    {
        var factory = new ApiFactory();
        await using var factoryScope = factory;
        var customer = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", DateTime.UtcNow);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, DateTime.UtcNow);
        factory.ProductStore.Seed(product);
        var invoice = Invoice.Create(customer.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, DateTime.UtcNow);
        invoice.AddLine(product, 1m, DateTime.UtcNow);
        Assert.True(invoice.Send(DateTime.UtcNow));
        await factory.InvoiceStore.AddAsync(invoice, default);
        using var admin = factory.CreateAuthorizedClient("Admin");

        var createProduct = await admin.PostAsync("/api/products", Json("{\"name\":\"Web hour\",\"sku\":\"WEB-1\",\"unit\":\"Hour\",\"unitPrice\":50,\"vatPercent\":15}"));
        var activateProduct = await admin.PutAsync($"/api/products/{product.Id}/active", Json("{\"isActive\":false}"));
        var cancelInvoice = await admin.PostAsync($"/api/invoices/{invoice.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Created, createProduct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, activateProduct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelInvoice.StatusCode);
        Assert.False(product.IsActive);
        Assert.Equal(InvoiceStatus.Cancelled, factory.InvoiceStore.All.Single(i => i.Id == invoice.Id).Status);
    }
}
