using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>The amounts an invoice response carries (spec 006): line net and VAT, the totals, and how they are written on the wire.</summary>
public class InvoiceTotalsEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Due(int daysFromToday = 30) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record World(ApiFactory Factory, Customer Abc, Product Consulting, Product Express);

    private static World NewWorld()
    {
        var factory = new ApiFactory();
        var abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(abc);
        var consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        var express = Product.Create("Express fee", "EXP-001", ProductUnit.Item, 50m, 0m, Now);
        factory.ProductStore.Seed(consulting);
        factory.ProductStore.Seed(express);
        return new World(factory, abc, consulting, express);
    }

    private static string Body(Guid customer, params string[] lines) =>
        $"{{\"customerId\":\"{customer}\",\"dueDate\":\"{Due()}\",\"notes\":\"Net 30\",\"lines\":[{string.Join(",", lines)}]}}";

    private static string NewLine(Guid product, string quantity = "2") => $"{{\"productId\":\"{product}\",\"quantity\":{quantity}}}";

    private static string KeepLine(JsonElement line, string quantity) => $"{{\"id\":\"{line.GetProperty("id").GetGuid()}\",\"quantity\":{quantity}}}";

    private static void AssertAcceptanceAmounts(JsonElement invoice)
    {
        Assert.Equal(0.00m, invoice.GetProperty("discount").GetDecimal());
        var totals = invoice.GetProperty("totals");
        Assert.Equal(200.00m, totals.GetProperty("totalNet").GetDecimal());
        Assert.Equal(0.00m, totals.GetProperty("discount").GetDecimal());
        Assert.Equal(200.00m, totals.GetProperty("netAfterDiscount").GetDecimal());
        Assert.Equal(30.00m, totals.GetProperty("totalVat").GetDecimal());
        Assert.Equal(230.00m, totals.GetProperty("grandTotal").GetDecimal());
        var line = Assert.Single(invoice.GetProperty("lines").EnumerateArray());
        Assert.Equal(200.00m, line.GetProperty("net").GetDecimal());
        Assert.Equal(0.00m, line.GetProperty("discountShare").GetDecimal());
        Assert.Equal(30.00m, line.GetProperty("vat").GetDecimal());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task Creating_an_invoice_for_two_at_100_with_15_percent_VAT_returns_net_200_VAT_30_grand_230(string role)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient(role);

        var response = await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertAcceptanceAmounts(await JsonOf(response));
    }

    [Fact]
    public async Task Opening_the_invoice_returns_the_same_amounts()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id)))));

        var response = await client.GetAsync($"/api/invoices/{created.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAcceptanceAmounts(await JsonOf(response));
    }

    [Fact]
    public async Task Amounts_are_written_as_JSON_numbers_with_two_decimals()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id))));

        var raw = (await JsonOf(response)).GetRawText();
        foreach (var expected in new[]
                 {
                     "\"discount\":0.00", "\"totalNet\":200.00", "\"netAfterDiscount\":200.00", "\"totalVat\":30.00",
                     "\"grandTotal\":230.00", "\"net\":200.00", "\"discountShare\":0.00", "\"vat\":30.00",
                 })
        {
            Assert.Contains(expected, raw);
        }
    }

    [Fact]
    public async Task Editing_an_invoice_returns_the_recalculated_amounts()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Abc.Id, KeepLine(created.GetProperty("lines")[0], "3"), NewLine(world.Express.Id, "1"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        var totals = body.GetProperty("totals");
        Assert.Equal(350.00m, totals.GetProperty("totalNet").GetDecimal());
        Assert.Equal(45.00m, totals.GetProperty("totalVat").GetDecimal());
        Assert.Equal(395.00m, totals.GetProperty("grandTotal").GetDecimal());
        Assert.Equal([300.00m, 50.00m], body.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("net").GetDecimal()));
        Assert.Equal([45.00m, 0.00m], body.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("vat").GetDecimal()));
    }

    [Fact]
    public async Task The_amounts_of_a_response_add_up()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var body = await JsonOf(await client.PostAsync(
            "/api/invoices",
            Json(Body(world.Abc.Id, NewLine(world.Consulting.Id, "3.33"), NewLine(world.Express.Id, "7")))));

        var totals = body.GetProperty("totals");
        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(lines.Sum(line => line.GetProperty("net").GetDecimal()), totals.GetProperty("totalNet").GetDecimal());
        Assert.Equal(lines.Sum(line => line.GetProperty("discountShare").GetDecimal()), totals.GetProperty("discount").GetDecimal());
        Assert.Equal(lines.Sum(line => line.GetProperty("vat").GetDecimal()), totals.GetProperty("totalVat").GetDecimal());
        Assert.Equal(totals.GetProperty("totalNet").GetDecimal() - totals.GetProperty("discount").GetDecimal(), totals.GetProperty("netAfterDiscount").GetDecimal());
        Assert.Equal(totals.GetProperty("netAfterDiscount").GetDecimal() + totals.GetProperty("totalVat").GetDecimal(), totals.GetProperty("grandTotal").GetDecimal());
    }

    // ---- the discount (story 2) ----

    private static string BodyWithDiscount(Guid customer, string? discount, params string[] lines) =>
        $"{{\"customerId\":\"{customer}\",\"dueDate\":\"{Due()}\",\"notes\":\"Net 30\"," +
        (discount is null ? string.Empty : $"\"discount\":{discount},") +
        $"\"lines\":[{string.Join(",", lines)}]}}";

    [Fact]
    public async Task Creating_with_a_discount_of_20_gives_180_27_207_and_the_line_carries_its_share()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");

        var response = await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, "20.00", NewLine(world.Consulting.Id))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(20.00m, body.GetProperty("discount").GetDecimal());
        var totals = body.GetProperty("totals");
        Assert.Equal(200.00m, totals.GetProperty("totalNet").GetDecimal());
        Assert.Equal(20.00m, totals.GetProperty("discount").GetDecimal());
        Assert.Equal(180.00m, totals.GetProperty("netAfterDiscount").GetDecimal());
        Assert.Equal(27.00m, totals.GetProperty("totalVat").GetDecimal());
        Assert.Equal(207.00m, totals.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(20.00m, body.GetProperty("lines")[0].GetProperty("discountShare").GetDecimal());
        Assert.Equal(27.00m, body.GetProperty("lines")[0].GetProperty("vat").GetDecimal());
    }

    [Fact]
    public async Task A_discount_over_lines_with_different_VAT_is_shared_in_proportion_and_the_shares_add_up_over_JSON()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync(
            "/api/invoices",
            Json(BodyWithDiscount(world.Abc.Id, "30.00", NewLine(world.Consulting.Id, "1"), NewLine(world.Express.Id, "1"))));

        var body = await JsonOf(response);
        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal([20.00m, 10.00m], lines.Select(line => line.GetProperty("discountShare").GetDecimal()));
        Assert.Equal([12.00m, 0.00m], lines.Select(line => line.GetProperty("vat").GetDecimal()));
        var totals = body.GetProperty("totals");
        Assert.Equal(lines.Sum(line => line.GetProperty("discountShare").GetDecimal()), totals.GetProperty("discount").GetDecimal());
        Assert.Equal(132.00m, totals.GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task A_discount_of_20_10_is_written_with_two_decimals_on_the_wire()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, "20.1", NewLine(world.Consulting.Id))));

        var raw = (await JsonOf(response)).GetRawText();
        foreach (var expected in new[] { "\"discount\":20.10", "\"netAfterDiscount\":179.90", "\"totalVat\":26.99", "\"grandTotal\":206.89", "\"discountShare\":20.10" })
        {
            Assert.Contains(expected, raw);
        }
    }

    [Fact]
    public async Task An_edit_whose_total_is_too_large_is_a_400_on_the_lines_and_changes_nothing()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        var big = Product.Create("Big price", "BIG-1", ProductUnit.Item, 999_999_999.99m, 0m, Now);
        world.Factory.ProductStore.Seed(big);
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();
        var before = await client.GetStringAsync($"/api/invoices/{id}");

        var response = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Abc.Id, KeepLine(created.GetProperty("lines")[0], "2"), NewLine(big.Id, "9000"), NewLine(big.Id, "9000"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.Equal("The invoice total is too large. Reduce the quantities or remove lines.", errors.GetProperty("lines")[0].GetString());
        Assert.Equal(before, await client.GetStringAsync($"/api/invoices/{id}"));
    }

    [Fact]
    public async Task An_edit_without_a_discount_clears_the_saved_one()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, "20.00", NewLine(world.Consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsync($"/api/invoices/{id}", Json(BodyWithDiscount(world.Abc.Id, null, KeepLine(created.GetProperty("lines")[0], "2"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAcceptanceAmounts(await JsonOf(response));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("20.001")]
    [InlineData("500")]
    public async Task A_bad_discount_is_a_400_on_the_discount_and_creates_nothing(string discount)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, discount, NewLine(world.Consulting.Id))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("discount", out _));
        Assert.Empty(world.Factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("20.001")]
    [InlineData("500")]
    public async Task A_bad_discount_on_an_edit_is_a_400_and_the_invoice_is_exactly_as_it_was(string discount)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, "20.00", NewLine(world.Consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();
        var before = (await client.GetStringAsync($"/api/invoices/{id}"));

        var response = await client.PutAsync($"/api/invoices/{id}", Json(BodyWithDiscount(world.Abc.Id, discount, KeepLine(created.GetProperty("lines")[0], "3"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("discount", out _));
        Assert.Equal(before, await client.GetStringAsync($"/api/invoices/{id}"));
    }

    [Fact]
    public async Task An_edit_that_removes_lines_so_the_saved_discount_is_now_above_the_net_is_a_400_and_changes_nothing()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await client.PostAsync(
            "/api/invoices",
            Json(BodyWithDiscount(world.Abc.Id, "100.00", NewLine(world.Consulting.Id, "2"), NewLine(world.Express.Id, "10")))));
        var id = created.GetProperty("id").GetGuid();
        var before = await client.GetStringAsync($"/api/invoices/{id}");

        // Net was 700.00; keeping only the express line at quantity 1 leaves 50.00, below the 100.00 discount.
        var response = await client.PutAsync($"/api/invoices/{id}", Json(BodyWithDiscount(world.Abc.Id, "100.00", KeepLine(created.GetProperty("lines")[1], "1"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.Equal("Discount cannot be more than the total net of the lines.", errors.GetProperty("discount")[0].GetString());
        Assert.Equal(before, await client.GetStringAsync($"/api/invoices/{id}"));
    }

    [Fact]
    public async Task A_discount_sent_as_text_is_the_generic_400_and_creates_nothing()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, "\"20\"", NewLine(world.Consulting.Id))));

        // Like a text quantity (005): a value of the wrong JSON type cannot be read at all, so it is a 400 without field names.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False((await JsonOf(response)).TryGetProperty("errors", out var errors) && errors.TryGetProperty("discount", out _));
        Assert.Empty(world.Factory.InvoiceStore.All);
    }

    [Fact]
    public async Task A_discount_equal_to_the_whole_net_is_accepted_over_HTTP_and_leaves_nothing_to_pay()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(BodyWithDiscount(world.Abc.Id, "200.00", NewLine(world.Consulting.Id))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var totals = (await JsonOf(response)).GetProperty("totals");
        Assert.Equal(0.00m, totals.GetProperty("netAfterDiscount").GetDecimal());
        Assert.Equal(0.00m, totals.GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task A_huge_quantity_is_a_400_on_that_lines_quantity_and_never_a_500()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        var huge = Product.Create("Huge price", "HUGE-1", ProductUnit.Item, 999_999_999.99m, 15m, Now);
        world.Factory.ProductStore.Seed(huge);
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(huge.Id, "999999.99"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.Equal("The amount of this line is too large. Reduce the quantity.", errors.GetProperty("lines[0].quantity")[0].GetString());
        Assert.Empty(world.Factory.InvoiceStore.All);
    }

    // ---- amounts stay true when products change (story 3) ----

    [Fact]
    public async Task Amounts_are_unchanged_after_an_Admin_reprices_and_deactivates_the_product_through_the_products_api()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var agent = world.Factory.CreateAuthorizedClient("Agent");
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await agent.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();

        var edit = await admin.PutAsync(
            $"/api/products/{world.Consulting.Id}",
            Json("""{"name":"Consulting day","sku":"CNS-002","unit":"Service","unitPrice":150,"vatPercent":5}"""));
        var off = await admin.PutAsync($"/api/products/{world.Consulting.Id}/active", Json("""{"isActive":false}"""));
        var reopened = await JsonOf(await agent.GetAsync($"/api/invoices/{id}"));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        AssertAcceptanceAmounts(reopened);
    }

    [Fact]
    public async Task Changing_the_quantity_after_a_repricing_uses_the_saved_price_300_45_345()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await admin.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();
        await admin.PutAsync(
            $"/api/products/{world.Consulting.Id}",
            Json("""{"name":"Consulting hour","sku":"CNS-001","unit":"Hour","unitPrice":150,"vatPercent":5}"""));

        var response = await admin.PutAsync($"/api/invoices/{id}", Json(Body(world.Abc.Id, KeepLine(created.GetProperty("lines")[0], "3"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var totals = (await JsonOf(response)).GetProperty("totals");
        Assert.Equal(300.00m, totals.GetProperty("totalNet").GetDecimal());
        Assert.Equal(45.00m, totals.GetProperty("totalVat").GetDecimal());
        Assert.Equal(345.00m, totals.GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task The_invoice_list_carries_no_amounts()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        await client.PostAsync("/api/invoices", Json(Body(world.Abc.Id, NewLine(world.Consulting.Id))));

        var page = await JsonOf(await client.GetAsync("/api/invoices"));

        var raw = page.GetProperty("items")[0].GetRawText();
        foreach (var forbidden in new[] { "totals", "discount", "grandTotal", "totalNet", "\"net\"", "\"vat\"" })
        {
            Assert.DoesNotContain(forbidden, raw);
        }
    }

    [Fact]
    public async Task Without_a_token_an_invoice_and_its_amounts_are_refused_with_401()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateClient();

        var response = await client.GetAsync($"/api/invoices/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
