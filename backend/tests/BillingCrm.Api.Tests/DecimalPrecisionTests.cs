using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Spec 012, User Story 5: every <c>decimal</c>-bound field (product price/VAT, invoice line quantity, invoice discount, payment
/// amount) refuses exponent notation and more than 28 significant digits at the door (<see cref="BillingCrm.Infrastructure.Serialization.DecimalJsonConverter"/>),
/// rather than silently rounding or shortening a financial value (Constitution IV). An ordinary number is unaffected.
/// </summary>
public class DecimalPrecisionTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string json) => await client.PostAsync(url, Json(json));

    private static string Day(int daysFromToday) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    // 29 digit characters: one more than the converter allows, regardless of where the decimal point falls.
    private const string TooManyDigits = "1234567890123456789012345678.90";

    private static void AssertRefused(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- product: unitPrice, vatPercent ----

    [Theory]
    [InlineData("1e5", "15")]
    [InlineData("100", "1E-3")]
    [InlineData(TooManyDigits, "15")]
    public async Task A_products_unitPrice_or_vatPercent_refuses_exponent_notation_or_too_many_digits(string unitPrice, string vatPercent)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await PostAsync(client, "/api/products",
            $$"""{"name":"X","sku":"{{Unique("SKU")}}","unit":"Hour","unitPrice":{{unitPrice}},"vatPercent":{{vatPercent}}}""");

        AssertRefused(response);
        Assert.Empty(factory.ProductStore.All);
    }

    [Fact]
    public async Task An_ordinary_product_price_and_vat_are_still_accepted()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await PostAsync(client, "/api/products",
            $$"""{"name":"X","sku":"{{Unique("SKU")}}","unit":"Hour","unitPrice":149.99,"vatPercent":15}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(factory.ProductStore.All);
    }

    // ---- invoice: line quantity, discount ----

    private static (ApiFactory Factory, HttpClient Client, Customer Customer, Product Product) InvoiceWorld()
    {
        var factory = new ApiFactory();
        var client = factory.CreateAuthorizedClient("Admin");
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(product);
        return (factory, client, customer, product);
    }

    [Theory]
    [InlineData("2e1", "0")]
    [InlineData("2", "1e1")]
    [InlineData(TooManyDigits, "0")]
    public async Task An_invoice_lines_quantity_or_the_discount_refuses_exponent_notation_or_too_many_digits(string quantity, string discount)
    {
        var (factory, client, customer, product) = InvoiceWorld();
        await using var factoryScope = factory;
        using var clientScope = client;

        var response = await PostAsync(client, "/api/invoices",
            $$"""{"customerId":"{{customer.Id}}","dueDate":"{{Day(30)}}","notes":null,"discount":{{discount}},"lines":[{"productId":"{{product.Id}}","quantity":{{quantity}}}]}""");

        AssertRefused(response);
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Fact]
    public async Task An_ordinary_invoice_quantity_and_discount_are_still_accepted()
    {
        var (factory, client, customer, product) = InvoiceWorld();
        await using var factoryScope = factory;
        using var clientScope = client;

        var response = await PostAsync(client, "/api/invoices",
            $$"""{"customerId":"{{customer.Id}}","dueDate":"{{Day(30)}}","notes":null,"discount":10.50,"lines":[{"productId":"{{product.Id}}","quantity":2.5}]}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(factory.InvoiceStore.All);
    }

    // ---- payment: amount ----

    private static async Task<(ApiFactory Factory, HttpClient Client, Invoice Invoice)> SentInvoiceWorldAsync()
    {
        var factory = new ApiFactory();
        var client = factory.CreateAuthorizedClient("Admin");
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(product);
        var invoice = Invoice.Create(customer.Id, DateOnly.Parse(Day(30)), null, Now);
        invoice.AddLine(product, 2m, Now);
        Assert.True(invoice.Send(Now));
        await factory.InvoiceStore.AddAsync(invoice, default);
        return (factory, client, invoice);
    }

    private static string TodaySaudi() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd");

    [Theory]
    [InlineData("1e1")]
    [InlineData(TooManyDigits)]
    public async Task A_payments_amount_refuses_exponent_notation_or_too_many_digits(string amount)
    {
        var (factory, client, invoice) = await SentInvoiceWorldAsync();
        await using var factoryScope = factory;
        using var clientScope = client;

        var response = await PostAsync(client, $"/api/invoices/{invoice.Id}/payments",
            $$"""{"date":"{{TodaySaudi()}}","amount":{{amount}},"method":"Cash","note":null}""");

        AssertRefused(response);
        Assert.Empty(factory.InvoiceStore.All.Single(candidate => candidate.Id == invoice.Id).Payments);
    }

    [Fact]
    public async Task An_ordinary_payment_amount_is_still_accepted()
    {
        var (factory, client, invoice) = await SentInvoiceWorldAsync();
        await using var factoryScope = factory;
        using var clientScope = client;

        var response = await PostAsync(client, $"/api/invoices/{invoice.Id}/payments",
            $$"""{"date":"{{TodaySaudi()}}","amount":50.00,"method":"Cash","note":null}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(factory.InvoiceStore.All.Single(candidate => candidate.Id == invoice.Id).Payments);
    }
}
