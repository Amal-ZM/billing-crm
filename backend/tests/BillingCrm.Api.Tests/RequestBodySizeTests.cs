using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Spec 012, User Story 5, research R4: a request body over the configured limit (1 MiB) is refused before any
/// endpoint or validator runs; a normal-size request body is unaffected. The largest legitimate body today is an
/// invoice with 100 lines, far under the limit.
/// </summary>
public class RequestBodySizeTests
{
    private const int OneMiB = 1_048_576;

    [Fact]
    public async Task A_body_just_over_the_limit_is_refused_with_413()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        // Padding inside a JSON string value so the body is syntactically well-formed up to the point it is cut off.
        var oversized = "{\"name\":\"" + new string('a', OneMiB) + "\"}";
        using var content = new StringContent(oversized, Encoding.UTF8, "application/json");
        content.Headers.ContentLength = oversized.Length;

        var response = await client.PostAsync("/api/products", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_normal_size_invoice_body_with_100_lines_is_unaffected()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");
        var customer = BillingCrm.Domain.Customer.Create("ABC", BillingCrm.Domain.CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", DateTime.UtcNow);
        factory.CustomerStore.Seed(customer);
        var products = Enumerable.Range(0, 100).Select(i =>
        {
            var product = BillingCrm.Domain.Product.Create($"Item {i}", $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), BillingCrm.Domain.ProductUnit.Item, 10m, 15m, DateTime.UtcNow);
            factory.ProductStore.Seed(product);
            return product;
        }).ToList();

        var lines = string.Join(",", products.Select(p => $$"""{"productId":"{{p.Id}}","quantity":1}"""));
        var due = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        var body = $$"""{"customerId":"{{customer.Id}}","dueDate":"{{due}}","notes":null,"lines":[{{lines}}]}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/invoices", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
