using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

public class InvoiceUpdateEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Due(int daysFromToday = 30) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record World(ApiFactory Factory, Customer Abc, Customer Other, Product Consulting, Product Web);

    private static World NewWorld()
    {
        var factory = new ApiFactory();
        var abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        var other = Customer.Create("Other Co", CustomerType.Company, "accounts@other.example", "0500000001", null, "Jeddah", Now);
        factory.CustomerStore.Seed(abc);
        factory.CustomerStore.Seed(other);
        var consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        var web = Product.Create("Website build", "WEB-9", ProductUnit.Service, 5000.50m, 0m, Now);
        factory.ProductStore.Seed(consulting);
        factory.ProductStore.Seed(web);
        return new World(factory, abc, other, consulting, web);
    }

    private static string Body(Guid customer, string due, string? notes, params string[] lines) =>
        $"{{\"customerId\":\"{customer}\",\"dueDate\":\"{due}\",\"notes\":{JsonSerializer.Serialize(notes)},\"lines\":[{string.Join(",", lines)}]}}";

    private static string NewLine(Guid product, string quantity = "1") => $"{{\"productId\":\"{product}\",\"quantity\":{quantity}}}";

    private static string KeepLine(JsonElement line, string? quantity = null) =>
        $"{{\"id\":\"{line.GetProperty("id").GetGuid()}\",\"quantity\":{quantity ?? line.GetProperty("quantity").GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";

    private static async Task<JsonElement> CreateAsync(HttpClient client, World world, params string[] lines)
    {
        var response = await client.PostAsync(
            "/api/invoices",
            Json(Body(world.Abc.Id, Due(), "Net 30", lines.Length == 0 ? [NewLine(world.Consulting.Id, "2")] : lines)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonOf(response);
    }

    // ---- two people editing at once ----

    [Fact]
    public async Task An_edit_that_lost_a_race_with_another_edit_is_a_409_problem_and_never_a_500()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var created = await CreateAsync(client, world);
        var id = created.GetProperty("id").GetGuid();
        world.Factory.InvoiceStore.SaveLosesRace = true;

        var response = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Abc.Id, Due(), "Changed", KeepLine(created.GetProperty("lines")[0], "5"))));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Invoice changed", body.GetProperty("title").GetString());
        Assert.Equal("This invoice was changed by someone else. Reload it and try again.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_edit_with_more_than_100_lines_is_refused_and_exactly_100_is_accepted()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(client, world);
        var id = created.GetProperty("id").GetGuid();

        var tooMany = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Abc.Id, Due(), null, Enumerable.Range(0, 101).Select(_ => NewLine(world.Consulting.Id)).ToArray())));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        var errors = (await JsonOf(tooMany)).GetProperty("errors");
        Assert.Equal("An invoice can have at most 100 lines.", errors.GetProperty("lines")[0].GetString());

        var enough = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Abc.Id, Due(), null, Enumerable.Range(0, 100).Select(_ => NewLine(world.Consulting.Id)).ToArray())));
        Assert.Equal(HttpStatusCode.OK, enough.StatusCode);
        Assert.Equal(100, (await JsonOf(enough)).GetProperty("lines").GetArrayLength());
    }

    // ---- a full edit ----

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task A_whole_Draft_edit_changes_quantity_adds_a_line_and_changes_due_date_notes_and_customer_for_either_role(string role)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient(role);
        var created = await CreateAsync(client, world);
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Other.Id, Due(60), "Pay on receipt", KeepLine(created.GetProperty("lines")[0], "3"), NewLine(world.Web.Id, "1.5"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(created.GetProperty("number").GetString(), body.GetProperty("number").GetString());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal("Other Co", body.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal("Pay on receipt", body.GetProperty("notes").GetString());
        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(3m, lines[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(100m, lines[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Website build", lines[1].GetProperty("productName").GetString());
        Assert.Equal(5000.50m, lines[1].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(1.5m, lines[1].GetProperty("quantity").GetDecimal());

        var reopened = await JsonOf(await client.GetAsync($"/api/invoices/{id}"));
        Assert.Equal(2, reopened.GetProperty("lines").GetArrayLength());
        Assert.Equal("Other Co", reopened.GetProperty("customer").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_kept_line_keeps_its_saved_price_after_the_product_changes_and_a_new_line_gets_the_new_price()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(client, world);
        await client.PutAsync($"/api/products/{world.Consulting.Id}", Json("""{"name":"Consulting hour","sku":"CNS-001","unit":"Hour","unitPrice":150,"vatPercent":0}"""));

        var response = await client.PutAsync(
            $"/api/invoices/{created.GetProperty("id").GetGuid()}",
            Json(Body(world.Abc.Id, Due(), "Net 30", KeepLine(created.GetProperty("lines")[0], "4"), NewLine(world.Consulting.Id, "1"))));

        var lines = (await JsonOf(response)).GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(100m, lines[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15m, lines[0].GetProperty("vatPercent").GetDecimal());
        Assert.Equal(150m, lines[1].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(0m, lines[1].GetProperty("vatPercent").GetDecimal());
    }

    [Fact]
    public async Task A_line_can_be_removed_while_another_remains()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var created = await CreateAsync(client, world, NewLine(world.Consulting.Id, "1"), NewLine(world.Web.Id, "2"));

        var response = await client.PutAsync(
            $"/api/invoices/{created.GetProperty("id").GetGuid()}",
            Json(Body(world.Abc.Id, Due(), "Net 30", KeepLine(created.GetProperty("lines")[1]))));

        var line = Assert.Single((await JsonOf(response)).GetProperty("lines").EnumerateArray());
        Assert.Equal("Website build", line.GetProperty("productName").GetString());
        Assert.Equal(2, line.GetProperty("position").GetInt32());
    }

    // ---- refusals leave the invoice untouched ----

    private static async Task<(HttpResponseMessage Response, JsonElement Before, ApiFactory Factory, HttpClient Client, Guid Id)> RefusedEditAsync(
        Func<World, JsonElement, string> body,
        Action<World>? arrange = null)
    {
        var world = NewWorld();
        var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(client, world, NewLine(world.Consulting.Id, "2"), NewLine(world.Web.Id, "1"));
        arrange?.Invoke(world);
        var id = created.GetProperty("id").GetGuid();
        var response = await client.PutAsync($"/api/invoices/{id}", Json(body(world, created)));
        return (response, created, world.Factory, client, id);
    }

    private static async Task AssertUnchangedAsync(HttpClient client, Guid id, JsonElement before)
    {
        var after = await JsonOf(await client.GetAsync($"/api/invoices/{id}"));
        Assert.Equal(before.GetProperty("updatedAtUtc").GetDateTime(), after.GetProperty("updatedAtUtc").GetDateTime());
        Assert.Equal(before.GetProperty("dueDate").GetString(), after.GetProperty("dueDate").GetString());
        Assert.Equal(before.GetProperty("notes").GetString(), after.GetProperty("notes").GetString());
        Assert.Equal(before.GetProperty("customer").GetProperty("id").GetGuid(), after.GetProperty("customer").GetProperty("id").GetGuid());
        Assert.Equal(before.GetProperty("lines").GetRawText(), after.GetProperty("lines").GetRawText());
    }

    [Fact]
    public async Task Removing_every_line_is_a_400_naming_lines_and_the_invoice_is_unchanged()
    {
        var (response, before, factory, client, id) = await RefusedEditAsync((w, _) => Body(w.Abc.Id, Due(), "Net 30"));
        await using var factoryScope = factory;
        using var clientScope = client;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("lines", out _));
        await AssertUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task A_new_line_for_an_inactive_product_and_a_bad_quantity_are_named_together_and_nothing_changes()
    {
        var (response, before, factory, client, id) = await RefusedEditAsync(
            (w, created) => Body(w.Abc.Id, Due(), "changed", KeepLine(created.GetProperty("lines")[0], "9"), NewLine(w.Web.Id, "0")),
            w => w.Web.SetActive(false, Now));
        await using var factoryScope = factory;
        using var clientScope = client;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lines[2].productId", out _) || errors.TryGetProperty("lines[1].productId", out _));
        Assert.True(errors.TryGetProperty("lines[1].quantity", out _));
        await AssertUnchangedAsync(client, id, before);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("both")]
    [InlineData("neither")]
    public async Task A_bad_line_identity_is_a_400_naming_the_line_and_nothing_changes(string kind)
    {
        var (response, before, factory, client, id) = await RefusedEditAsync((w, created) =>
        {
            var first = created.GetProperty("lines")[0];
            var line = kind switch
            {
                "foreign" => $"{{\"id\":\"{Guid.NewGuid()}\",\"quantity\":1}}",
                "unknown" => "{\"id\":\"not-a-guid\",\"quantity\":1}",
                "duplicate" => KeepLine(first) + "," + KeepLine(first),
                "both" => $"{{\"id\":\"{first.GetProperty("id").GetGuid()}\",\"productId\":\"{w.Web.Id}\",\"quantity\":1}}",
                _ => "{\"quantity\":1}",
            };
            return Body(w.Abc.Id, Due(), "Net 30", line);
        });
        await using var factoryScope = factory;
        using var clientScope = client;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
        Assert.Contains(errors, name => name.StartsWith("lines[", StringComparison.Ordinal));
        await AssertUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task A_different_inactive_customer_is_refused_and_the_same_customer_is_allowed_even_when_inactive()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var created = await CreateAsync(client, world);
        var id = created.GetProperty("id").GetGuid();
        var keep = KeepLine(created.GetProperty("lines")[0], "5");
        world.Other.SetActive(false, Now);
        world.Abc.SetActive(false, Now);

        var toInactive = await client.PutAsync($"/api/invoices/{id}", Json(Body(world.Other.Id, Due(), "Net 30", keep)));
        var sameInactive = await client.PutAsync($"/api/invoices/{id}", Json(Body(world.Abc.Id, Due(), "Net 30", keep)));

        Assert.Equal(HttpStatusCode.BadRequest, toInactive.StatusCode);
        Assert.True((await JsonOf(toInactive)).GetProperty("errors").TryGetProperty("customerId", out _));
        Assert.Equal(HttpStatusCode.OK, sameInactive.StatusCode);
        var body = await JsonOf(sameInactive);
        Assert.False(body.GetProperty("customer").GetProperty("isActive").GetBoolean());
        Assert.Equal(5m, body.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task A_Draft_whose_product_was_deactivated_through_the_products_api_can_still_be_edited_but_gets_no_new_line_for_it()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(admin, world);
        var id = created.GetProperty("id").GetGuid();
        await admin.PutAsync($"/api/products/{world.Consulting.Id}/active", Json("""{"isActive":false}"""));

        var kept = await admin.PutAsync($"/api/invoices/{id}", Json(Body(world.Abc.Id, Due(), "Net 30", KeepLine(created.GetProperty("lines")[0], "8"))));
        var added = await admin.PutAsync(
            $"/api/invoices/{id}",
            Json(Body(world.Abc.Id, Due(), "Net 30", KeepLine(created.GetProperty("lines")[0]), NewLine(world.Consulting.Id))));

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(8m, (await JsonOf(kept)).GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        Assert.True((await JsonOf(added)).GetProperty("errors").TryGetProperty("lines[1].productId", out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1000000")]
    public async Task Each_quantity_rule_applies_on_edit_too(string quantity)
    {
        var (response, before, factory, client, id) = await RefusedEditAsync(
            (w, created) => Body(w.Abc.Id, Due(), "Net 30", KeepLine(created.GetProperty("lines")[0], quantity)));
        await using var factoryScope = factory;
        using var clientScope = client;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("lines[0].quantity", out _));
        await AssertUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task A_changed_past_due_date_is_a_400_naming_dueDate()
    {
        var (response, before, factory, client, id) = await RefusedEditAsync(
            (w, created) => Body(w.Abc.Id, "2000-01-01", "Net 30", KeepLine(created.GetProperty("lines")[0]), KeepLine(created.GetProperty("lines")[1])));
        await using var factoryScope = factory;
        using var clientScope = client;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("dueDate", out _));
        await AssertUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_is_a_200_and_does_not_move_the_changed_time()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(client, world);
        var dueDate = created.GetProperty("dueDate").GetString()!;

        var response = await client.PutAsync(
            $"/api/invoices/{created.GetProperty("id").GetGuid()}",
            Json(Body(world.Abc.Id, dueDate, "Net 30", KeepLine(created.GetProperty("lines")[0]))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("updatedAtUtc").GetDateTime(), (await JsonOf(response)).GetProperty("updatedAtUtc").GetDateTime());
    }

    // ---- not found, malformed, access ----

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2b9c1e-1111-4222-8333-444455556666")]
    [InlineData("not-a-guid")]
    public async Task An_unknown_or_malformed_id_is_a_404_even_when_the_body_is_wrong(string id)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var good = await client.PutAsync($"/api/invoices/{id}", Json(Body(world.Abc.Id, Due(), "Net 30", NewLine(world.Consulting.Id))));
        var bad = await client.PutAsync($"/api/invoices/{id}", Json("""{"lines":[]}"""));

        Assert.Equal(HttpStatusCode.NotFound, good.StatusCode);
        Assert.Equal("Invoice not found", (await JsonOf(good)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"customerId":"x","dueDate":"2099-01-01","lines":[{"id":"y","quantity":"two"}]}""")]
    public async Task An_empty_or_malformed_body_is_a_400_problem_details(string body)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(client, world);

        var response = await client.PutAsync($"/api/invoices/{created.GetProperty("id").GetGuid()}", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task There_is_no_delete_and_a_change_without_a_token_is_a_401()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(admin, world);
        var id = created.GetProperty("id").GetGuid();
        using var anonymous = world.Factory.CreateClient();

        var noToken = await anonymous.PutAsync($"/api/invoices/{id}", Json(Body(world.Abc.Id, Due(), "Hacked", NewLine(world.Consulting.Id))));
        var delete = await admin.DeleteAsync($"/api/invoices/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.True(delete.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert.Equal("Net 30", (await JsonOf(await admin.GetAsync($"/api/invoices/{id}"))).GetProperty("notes").GetString());
    }
}
