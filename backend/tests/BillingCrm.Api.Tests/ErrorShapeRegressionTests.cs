using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Spec 012, User Story 2: one 400/404/409 example per resource, consolidated in one file, all with the same
/// <c>ProblemDetails</c> shape — proof that "expected problems always look the same" holds across the whole application, not just the
/// resource whose own spec happened to test it.
/// </summary>
public class ErrorShapeRegressionTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void AssertProblemShape(JsonElement body, bool expectErrors)
    {
        var names = body.EnumerateObject().Select(property => property.Name).ToList();
        Assert.Contains("title", names);
        Assert.Contains("detail", names);
        Assert.Equal(expectErrors, names.Contains("errors"));
        // No internal Error.Code or stack trace ever leaks into the body (Constitution III).
        Assert.DoesNotContain("code", names);
        Assert.DoesNotContain("stackTrace", names);
    }

    // ---- 400: an invalid field, one per resource ----

    [Fact]
    public async Task A_bad_field_is_400_naming_it_on_every_resource()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var badCustomer = await client.PostAsync("/api/customers", Json("{\"name\":\"\",\"type\":\"Company\",\"email\":\"a@b.com\",\"phone\":\"1\",\"address\":\"x\"}"));
        var badProduct = await client.PostAsync("/api/products", Json("{\"name\":\"X\",\"sku\":\"X-1\",\"unit\":\"Hour\",\"unitPrice\":-1,\"vatPercent\":15}"));
        var badInvoice = await client.PostAsync("/api/invoices", Json("{\"customerId\":null,\"dueDate\":\"not-a-date\",\"lines\":[]}"));

        Assert.Equal(HttpStatusCode.BadRequest, badCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badProduct.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badInvoice.StatusCode);
        var customerBody = await BodyOf(badCustomer);
        var productBody = await BodyOf(badProduct);
        var invoiceBody = await BodyOf(badInvoice);
        AssertProblemShape(customerBody, expectErrors: true);
        AssertProblemShape(productBody, expectErrors: true);
        AssertProblemShape(invoiceBody, expectErrors: true);
        Assert.True(customerBody.GetProperty("errors").TryGetProperty("name", out _));
        Assert.True(productBody.GetProperty("errors").TryGetProperty("unitPrice", out _));
        Assert.True(invoiceBody.GetProperty("errors").TryGetProperty("dueDate", out _));
    }

    // ---- 404: an unknown id, one per resource ----

    [Fact]
    public async Task An_unknown_id_is_404_on_every_resource()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");
        var missing = Guid.NewGuid();

        var customer = await client.GetAsync($"/api/customers/{missing}");
        var product = await client.GetAsync($"/api/products/{missing}");
        var invoice = await client.GetAsync($"/api/invoices/{missing}");

        Assert.Equal(HttpStatusCode.NotFound, customer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, product.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invoice.StatusCode);
        AssertProblemShape(await BodyOf(customer), expectErrors: false);
        AssertProblemShape(await BodyOf(product), expectErrors: false);
        AssertProblemShape(await BodyOf(invoice), expectErrors: false);
    }

    // ---- 409: a duplicate unique value or an illegal status change, one per resource ----

    [Fact]
    public async Task A_conflict_is_409_on_every_resource()
    {
        var factory = new ApiFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Admin");

        var email = $"{Guid.NewGuid():N}@example.test";
        await client.PostAsync("/api/customers", Json($"{{\"name\":\"A\",\"type\":\"Company\",\"email\":\"{email}\",\"phone\":\"0500000000\",\"address\":\"x\"}}"));
        var duplicateCustomer = await client.PostAsync("/api/customers", Json($"{{\"name\":\"B\",\"type\":\"Company\",\"email\":\"{email}\",\"phone\":\"0500000001\",\"address\":\"y\"}}"));

        var sku = Unique("SKU");
        await client.PostAsync("/api/products", Json($"{{\"name\":\"A\",\"sku\":\"{sku}\",\"unit\":\"Hour\",\"unitPrice\":10,\"vatPercent\":15}}"));
        var duplicateProduct = await client.PostAsync("/api/products", Json($"{{\"name\":\"B\",\"sku\":\"{sku}\",\"unit\":\"Hour\",\"unitPrice\":20,\"vatPercent\":15}}"));

        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(product);
        var invoice = Invoice.Create(customer.Id, DateOnly.FromDateTime(Now.AddDays(30)), null, Now);
        invoice.AddLine(product, 1m, Now);
        Assert.True(invoice.Cancel(Now));
        await factory.InvoiceStore.AddAsync(invoice, default);
        var illegalSend = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null); // a Cancelled invoice cannot be sent

        Assert.Equal(HttpStatusCode.Conflict, duplicateCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateProduct.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, illegalSend.StatusCode);
        AssertProblemShape(await BodyOf(duplicateCustomer), expectErrors: false);
        AssertProblemShape(await BodyOf(duplicateProduct), expectErrors: false);
        AssertProblemShape(await BodyOf(illegalSend), expectErrors: false);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
}
