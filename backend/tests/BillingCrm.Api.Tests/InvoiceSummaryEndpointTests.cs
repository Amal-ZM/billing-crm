using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>The Home dashboard's summary over HTTP (spec 011, contracts/dashboard-api.md).</summary>
public class InvoiceSummaryEndpointTests
{
    // The real InvoiceService reads the real clock (ApiFactory registers no fake TimeProvider), so "overdue" here is judged against
    // the actual current date, not a fixed one: a due date is always built relative to DateTime.UtcNow, exactly as InvoiceStatusEndpointTests does.
    private static readonly DateTime Now = DateTime.UtcNow;

    private static string Day(int daysFromToday) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> Summary(HttpClient client) => client.GetAsync("/api/invoices/summary");

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Both_roles_read_the_same_five_figures_and_nothing_else(string role)
    {
        var factory = new ApiFactory();
        await using var factoryScope = factory;
        var customer = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(customer);
        var product = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(product);

        var draft = Invoice.Create(customer.Id, DateOnly.Parse(Day(30)), null, Now);
        draft.AddLine(product, 1m, Now);
        await factory.InvoiceStore.AddAsync(draft, default);

        var overdue = Invoice.Create(customer.Id, DateOnly.Parse(Day(-3)), null, Now);
        overdue.AddLine(product, 2m, Now);
        Assert.True(overdue.Send(Now));
        await factory.InvoiceStore.AddAsync(overdue, default);

        using var client = factory.CreateAuthorizedClient(role);
        var response = await Summary(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(["draftCount", "unpaidCount", "overdueCount", "paidThisMonthCount", "unpaidAmount"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, body.GetProperty("draftCount").GetInt32());
        Assert.Equal(1, body.GetProperty("unpaidCount").GetInt32());
        Assert.Equal(1, body.GetProperty("overdueCount").GetInt32());
        Assert.Equal(0, body.GetProperty("paidThisMonthCount").GetInt32());
        Assert.Equal(230.00m, body.GetProperty("unpaidAmount").GetDecimal());
    }

    [Fact]
    public async Task An_empty_store_gives_every_figure_zero()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Agent");

        var body = await JsonOf(await Summary(client));

        Assert.Equal(0, body.GetProperty("draftCount").GetInt32());
        Assert.Equal(0, body.GetProperty("unpaidCount").GetInt32());
        Assert.Equal(0, body.GetProperty("overdueCount").GetInt32());
        Assert.Equal(0, body.GetProperty("paidThisMonthCount").GetInt32());
        Assert.Equal(0m, body.GetProperty("unpaidAmount").GetDecimal());
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Summary(client)).StatusCode);
    }

    [Fact]
    public async Task Reading_the_summary_changes_nothing()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Agent");

        await Summary(client);
        await Summary(client);

        Assert.Empty(factory.InvoiceStore.All);
    }
}
