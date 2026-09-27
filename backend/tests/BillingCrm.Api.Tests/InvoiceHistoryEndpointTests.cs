using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>Reading an invoice's history over HTTP (spec 010, contracts/invoice-history-api.md).</summary>
public class InvoiceHistoryEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Day(int daysFromToday) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

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

    private static async Task<Guid> CreateAsync(HttpClient client, World world)
    {
        var body = $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"Net 30\",\"lines\":[{{\"productId\":\"{world.Consulting.Id}\",\"quantity\":2}}]}}";
        var response = await client.PostAsync("/api/invoices", Json(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> History(HttpClient client, object id) => client.GetAsync($"/api/invoices/{id}/history");

    // ---- who can read, and the shape ----

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Any_signed_in_user_can_read_the_history_with_exactly_the_documented_shape(string role)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var writer = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(writer, world);
        using var reader = factory.CreateAuthorizedClient(role);

        var response = await History(reader, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(["number", "startsWithCreation", "entries"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal("INV-000001", body.GetProperty("number").GetString());
        Assert.True(body.GetProperty("startsWithCreation").GetBoolean());
        var entry = Assert.Single(body.GetProperty("entries").EnumerateArray());
        Assert.Equal(["id", "kind", "occurredAtUtc", "actorEmail", "actorRole", "changes"], entry.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Created", entry.GetProperty("kind").GetString());
        Assert.Equal("agent@crm.test", entry.GetProperty("actorEmail").GetString());
        Assert.Equal("Agent", entry.GetProperty("actorRole").GetString());
        Assert.True(entry.GetProperty("occurredAtUtc").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
        var change = entry.GetProperty("changes").EnumerateArray().First();
        Assert.Equal(["field", "before", "after"], change.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Customer", change.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("before").ValueKind);
        Assert.Equal("ABC", change.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Entries_are_newest_first_after_the_write_routes_are_used_over_HTTP()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(client, world);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/invoices/{id}/send", null)).StatusCode);
        var today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync($"/api/invoices/{id}/payments", Json($"{{\"date\":\"{today}\",\"amount\":100,\"method\":\"Cash\"}}"))).StatusCode);

        var body = await JsonOf(await History(client, id));

        Assert.Equal(["Payment", "Sent", "Created"], body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task The_worked_example_send_as_the_Agent_then_read_shows_a_Sent_row_with_the_Agents_email_and_role()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var agent = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(agent, world);
        await agent.PostAsync($"/api/invoices/{id}/send", null);

        var body = await JsonOf(await History(agent, id));

        var sent = body.GetProperty("entries")[0];
        Assert.Equal("Sent", sent.GetProperty("kind").GetString());
        Assert.Equal("agent@crm.test", sent.GetProperty("actorEmail").GetString());
        Assert.Equal("Agent", sent.GetProperty("actorRole").GetString());
        var status = Assert.Single(sent.GetProperty("changes").EnumerateArray());
        Assert.Equal("Status", status.GetProperty("field").GetString());
        Assert.Equal("Draft", status.GetProperty("before").GetString());
        Assert.Equal("Sent", status.GetProperty("after").GetString());
    }

    [Fact]
    public async Task An_invoice_with_no_entries_answers_200_with_startsWithCreation_false_and_no_entries()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var invoice = Invoice.Create(world.Abc.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, Now);
        invoice.AddLine(world.Consulting, 1m, Now);
        await factory.InvoiceStore.AddAsync(invoice, default);
        using var client = factory.CreateAuthorizedClient("Agent");

        var response = await History(client, invoice.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.False(body.GetProperty("startsWithCreation").GetBoolean());
        Assert.Empty(body.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task A_query_string_is_ignored()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(client, world);
        await client.PostAsync($"/api/invoices/{id}/send", null);

        var plain = await JsonOf(await History(client, id));
        var withQuery = await JsonOf(await client.GetAsync($"/api/invoices/{id}/history?page=2&sort=asc&q=x&pageSize=1"));

        Assert.Equal(plain.GetRawText(), withQuery.GetRawText());
    }

    // ---- refusals ----

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var writer = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(writer, world);
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await History(anonymous, id)).StatusCode);
    }

    [Fact]
    public async Task An_expired_or_tampered_token_is_401()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var writer = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(writer, world);

        foreach (var token in new[] { TestTokens.Expired(), TestTokens.Tampered() })
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await History(client, id)).StatusCode);
        }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2b9d3e-7d0e-4a0f-9c55-0d5a4b6a7e10")]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task An_unknown_or_malformed_id_is_404_invoice_not_found(string id)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");

        var response = await History(client, id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Invoice not found", (await JsonOf(response)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task There_is_no_way_to_write_history_over_HTTP(string method)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Admin");
        var id = await CreateAsync(client, world);
        var before = factory.InvoiceStore.HistoryOf(id).Count;

        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/invoices/{id}/history") { Content = Json("{\"kind\":\"Sent\"}") };
        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{method} answered {(int)response.StatusCode}");
        Assert.Equal(before, factory.InvoiceStore.HistoryOf(id).Count);
    }

    [Fact]
    public async Task A_single_entry_cannot_be_read_or_changed_by_id()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Admin");
        var id = await CreateAsync(client, world);
        var entryId = factory.InvoiceStore.HistoryOf(id).Single().Id;

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, $"/api/invoices/{id}/history/{entryId}"));
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{method} answered {(int)response.StatusCode}");
        }

        Assert.Single(factory.InvoiceStore.HistoryOf(id));
    }

    // ---- what the body does not contain ----

    [Fact]
    public async Task The_body_has_no_invoice_id_customer_id_or_user_id_only_the_text_copied_at_the_time()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(client, world);
        await client.PostAsync($"/api/invoices/{id}/send", null);

        var text = (await client.GetStringAsync($"/api/invoices/{id}/history")).ToLowerInvariant();

        Assert.DoesNotContain(id.ToString(), text);
        Assert.DoesNotContain(world.Abc.Id.ToString(), text);
        Assert.DoesNotContain(world.Consulting.Id.ToString(), text);
        Assert.DoesNotContain("userid", text);
        Assert.DoesNotContain("invoiceid", text);
        Assert.DoesNotContain("customerid", text);
    }

    [Fact]
    public async Task Reading_history_writes_nothing()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = await CreateAsync(client, world);
        var before = factory.InvoiceStore.HistoryOf(id).Count;

        await History(client, id);
        await History(client, id);
        await History(client, Guid.NewGuid());

        Assert.Equal(before, factory.InvoiceStore.HistoryOf(id).Count);
    }
}
