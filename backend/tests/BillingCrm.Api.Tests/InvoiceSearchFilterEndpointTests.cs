using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>The invoice list's four dashboard views (spec 011, contracts/dashboard-api.md).</summary>
public class InvoiceSearchFilterEndpointTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static string Day(int daysFromToday) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<List<Guid>> IdsOf(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/invoices{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    private sealed record World(ApiFactory Factory, Customer Abc, Product Consulting);

    private static World NewWorld()
    {
        var factory = new ApiFactory();
        var abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(abc);
        var consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(consulting);
        return new World(factory, abc, consulting);
    }

    private static Invoice Seed(World world, InvoiceStatus status, string dueDate)
    {
        var invoice = Invoice.Create(world.Abc.Id, DateOnly.Parse(dueDate), null, Now);
        invoice.AddLine(world.Consulting, 2m, Now);
        if (status is InvoiceStatus.Sent or InvoiceStatus.Cancelled)
        {
            Assert.True(status == InvoiceStatus.Sent ? invoice.Send(Now) : invoice.Cancel(Now));
        }

        world.Factory.InvoiceStore.AddAsync(invoice, default).GetAwaiter().GetResult();
        return invoice;
    }

    [Fact]
    public async Task Status_Draft_returns_exactly_the_Draft_invoices()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var draft = Seed(world, InvoiceStatus.Draft, Day(30));
        var sent = Seed(world, InvoiceStatus.Sent, Day(30));
        using var client = factory.CreateAuthorizedClient("Agent");

        var ids = await IdsOf(client, "?status=Draft");

        Assert.Contains(draft.Id, ids);
        Assert.DoesNotContain(sent.Id, ids);
    }

    [Fact]
    public async Task Status_Sent_returns_every_Sent_invoice_on_time_and_overdue_alike()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var onTime = Seed(world, InvoiceStatus.Sent, Day(30));
        var overdue = Seed(world, InvoiceStatus.Sent, Day(-3));
        var draft = Seed(world, InvoiceStatus.Draft, Day(30));
        using var client = factory.CreateAuthorizedClient("Agent");

        var ids = await IdsOf(client, "?status=Sent");

        Assert.Contains(onTime.Id, ids);
        Assert.Contains(overdue.Id, ids);
        Assert.DoesNotContain(draft.Id, ids);
    }

    [Fact]
    public async Task Status_Sent_with_overdue_true_returns_only_the_overdue_ones()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var onTime = Seed(world, InvoiceStatus.Sent, Day(30));
        var overdue = Seed(world, InvoiceStatus.Sent, Day(-3));
        using var client = factory.CreateAuthorizedClient("Agent");

        var ids = await IdsOf(client, "?status=Sent&overdue=true");

        Assert.Contains(overdue.Id, ids);
        Assert.DoesNotContain(onTime.Id, ids);
    }

    [Fact]
    public async Task Status_Paid_with_paidMonth_true_returns_only_invoices_settled_this_month()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var thisMonth = Seed(world, InvoiceStatus.Sent, Day(30));
        thisMonth.RecordPayment(DateOnly.FromDateTime(DateTime.UtcNow), 230.00m, PaymentMethod.Cash, null, Now);
        var lastMonth = Seed(world, InvoiceStatus.Sent, Day(30));
        lastMonth.RecordPayment(DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), 230.00m, PaymentMethod.Cash, null, Now.AddMonths(-1));
        using var client = factory.CreateAuthorizedClient("Agent");

        var ids = await IdsOf(client, "?status=Paid&paidMonth=true");

        Assert.Contains(thisMonth.Id, ids);
        Assert.DoesNotContain(lastMonth.Id, ids);
    }

    [Theory]
    [InlineData("not-a-status")]
    [InlineData("draft")] // wrong case
    [InlineData("0")] // the enum's own underlying number (Draft); Enum.TryParse alone would accept it
    public async Task An_unrecognized_status_is_400_naming_status(string status)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");

        var response = await client.GetAsync($"/api/invoices?status={status}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonOf(response);
        Assert.True(body.GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Overdue_with_no_status_or_the_wrong_status_changes_nothing()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var draft = Seed(world, InvoiceStatus.Draft, Day(30));
        var sent = Seed(world, InvoiceStatus.Sent, Day(-3));
        using var client = factory.CreateAuthorizedClient("Agent");

        var plain = await IdsOf(client, string.Empty);
        var withStrayOverdue = await IdsOf(client, "?overdue=true");
        var draftWithOverdue = await IdsOf(client, "?status=Draft&overdue=true");

        Assert.Equal(plain.OrderBy(id => id), withStrayOverdue.OrderBy(id => id));
        Assert.Contains(draft.Id, draftWithOverdue);
        Assert.DoesNotContain(sent.Id, draftWithOverdue);
    }

    [Fact]
    public async Task Filtering_and_paging_both_apply_together()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        for (var i = 0; i < 3; i++)
        {
            Seed(world, InvoiceStatus.Draft, Day(30));
        }

        using var client = factory.CreateAuthorizedClient("Agent");
        var response = await client.GetAsync("/api/invoices?status=Draft&page=1&pageSize=2");
        var body = await JsonOf(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task With_none_of_the_new_values_the_response_is_exactly_what_it_was_before_this_spec()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        Seed(world, InvoiceStatus.Draft, Day(30));
        using var client = factory.CreateAuthorizedClient("Agent");

        var response = await client.GetAsync("/api/invoices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(["items", "page", "pageSize", "totalCount", "totalPages"], body.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }
}
