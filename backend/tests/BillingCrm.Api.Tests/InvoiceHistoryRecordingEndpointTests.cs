using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// The user on a history entry over HTTP (spec 010): it is the token's email and role and nothing the request says, and a refused request
/// writes nothing. The in-memory store keeps the entries so the tests can read them.
/// </summary>
public class InvoiceHistoryRecordingEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private const string AgentEmail = "agent@crm.test";
    private const string AdminEmail = "admin@crm.test";

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

    private static string CreateBody(World world, string? extra = null) =>
        $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"Net 30\",\"lines\":[{{\"productId\":\"{world.Consulting.Id}\",\"quantity\":2}}]{extra}}}";

    private static async Task<JsonElement> CreateAsync(HttpClient client, World world, string? extra = null)
    {
        var response = await client.PostAsync("/api/invoices", Json(CreateBody(world, extra)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonOf(response);
    }

    private static Guid IdOf(JsonElement invoice) => invoice.GetProperty("id").GetGuid();

    private static IReadOnlyList<InvoiceHistoryEntry> History(World world, Guid id) => world.Factory.InvoiceStore.HistoryOf(id);

    private static string EditBody(World world, JsonElement invoice, string notes, string? extra = null) =>
        $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"{notes}\",\"lines\":[{{\"id\":\"{invoice.GetProperty("lines")[0].GetProperty("id").GetGuid()}\",\"quantity\":2}}]{extra}}}";

    // ---- the user is the token's ----

    [Theory]
    [InlineData("Agent", AgentEmail, StaffRole.Agent)]
    [InlineData("Admin", AdminEmail, StaffRole.Admin)]
    public async Task Create_edit_send_and_pay_record_the_email_and_role_of_the_token(string role, string email, StaffRole expectedRole)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient(role);

        var created = await CreateAsync(client, world);
        var id = IdOf(created);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/invoices/{id}", Json(EditBody(world, created, "Pay on receipt")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/invoices/{id}/send", null)).StatusCode);
        var today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync($"/api/invoices/{id}/payments", Json($"{{\"date\":\"{today}\",\"amount\":100,\"method\":\"Cash\"}}"))).StatusCode);

        var entries = History(world, id);
        Assert.Equal([HistoryKind.Created, HistoryKind.DraftEdited, HistoryKind.Sent, HistoryKind.Payment], entries.Select(entry => entry.Kind));
        Assert.All(entries, entry =>
        {
            Assert.Equal(email, entry.ActorEmail);
            Assert.Equal(expectedRole, entry.ActorRole);
        });
    }

    [Fact]
    public async Task An_Admin_cancelling_is_recorded_as_that_Admin()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var agent = factory.CreateAuthorizedClient("Agent");
        using var admin = factory.CreateAuthorizedClient("Admin");
        var id = IdOf(await CreateAsync(agent, world));

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/invoices/{id}/cancel", null)).StatusCode);

        var entries = History(world, id);
        Assert.Equal(2, entries.Count);
        Assert.Equal((AgentEmail, StaffRole.Agent), (entries[0].ActorEmail, entries[0].ActorRole));
        Assert.Equal(HistoryKind.Cancelled, entries[1].Kind);
        Assert.Equal((AdminEmail, StaffRole.Admin), (entries[1].ActorEmail, entries[1].ActorRole));
    }

    // ---- a request cannot name another user ----

    [Fact]
    public async Task Body_fields_and_headers_naming_another_user_change_nothing()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        client.DefaultRequestHeaders.Add("X-Actor", AdminEmail);
        client.DefaultRequestHeaders.Add("X-Actor-Email", AdminEmail);
        client.DefaultRequestHeaders.Add("X-Actor-Role", "Admin");
        var extra = $",\"actorEmail\":\"{AdminEmail}\",\"actorRole\":\"Admin\",\"user\":\"{AdminEmail}\",\"recordedBy\":\"{AdminEmail}\"";

        var created = await CreateAsync(client, world, extra);
        var id = IdOf(created);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/invoices/{id}", Json(EditBody(world, created, "changed", extra)))).StatusCode);

        var entries = History(world, id);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(AgentEmail, entry.ActorEmail);
            Assert.Equal(StaffRole.Agent, entry.ActorRole);
        });
    }

    [Fact]
    public async Task A_payment_body_naming_another_user_changes_nothing()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = IdOf(await CreateAsync(client, world));
        await client.PostAsync($"/api/invoices/{id}/send", null);
        var today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");

        var response = await client.PostAsync(
            $"/api/invoices/{id}/payments",
            Json($"{{\"date\":\"{today}\",\"amount\":50,\"method\":\"Bank\",\"actorEmail\":\"{AdminEmail}\",\"recordedBy\":\"{AdminEmail}\"}}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = History(world, id).Single(entry => entry.Kind == HistoryKind.Payment);
        Assert.Equal((AgentEmail, StaffRole.Agent), (payment.ActorEmail, payment.ActorRole));
    }

    // ---- refusals write nothing ----

    [Fact]
    public async Task Every_refusal_leaves_the_history_unchanged()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var agent = factory.CreateAuthorizedClient("Agent");
        var id = IdOf(await CreateAsync(agent, world));
        var missing = Guid.NewGuid();

        var refusals = new[]
        {
            (await agent.PostAsync("/api/invoices", Json("{\"customerId\":null,\"dueDate\":\"bad\",\"lines\":[]}"))).StatusCode, // 400
            (await agent.PostAsync($"/api/invoices/{id}/cancel", null)).StatusCode, // 403: an Agent cannot cancel
            (await agent.PostAsync($"/api/invoices/{missing}/send", null)).StatusCode, // 404
            (await agent.PostAsync($"/api/invoices/{id}/payments", Json($"{{\"date\":\"{Day(0)}\",\"amount\":10,\"method\":\"Cash\"}}"))).StatusCode, // 409: a Draft takes no payment
            (await agent.PutAsync($"/api/invoices/{missing}", Json(CreateBody(world)))).StatusCode, // 404
        };

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.NotFound], refusals);
        Assert.Equal(HistoryKind.Created, Assert.Single(History(world, id)).Kind);
        Assert.Empty(History(world, missing));
        Assert.Single(world.Factory.InvoiceStore.All); // the refused create made no invoice
    }

    [Fact]
    public async Task A_locked_edit_and_a_repeated_send_are_conflicts_that_write_nothing()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var created = await CreateAsync(client, world);
        var id = IdOf(created);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/invoices/{id}/send", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync($"/api/invoices/{id}", Json(EditBody(world, created, "late")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/invoices/{id}/send", null)).StatusCode);

        Assert.Equal([HistoryKind.Created, HistoryKind.Sent], History(world, id).Select(entry => entry.Kind));
    }

    [Fact]
    public async Task A_lost_race_is_a_conflict_that_writes_no_entry()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = IdOf(await CreateAsync(client, world));
        factory.InvoiceStore.SaveLosesRace = true;

        var response = await client.PostAsync($"/api/invoices/{id}/send", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HistoryKind.Created, Assert.Single(History(world, id)).Kind);
    }

    // ---- the shape of the other routes ----

    [Fact]
    public async Task An_invoice_put_straight_into_the_store_has_no_entries()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        var invoice = Invoice.Create(world.Abc.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, Now);
        invoice.AddLine(world.Consulting, 1m, Now);
        await factory.InvoiceStore.AddAsync(invoice, default);

        Assert.Empty(History(world, invoice.Id));
    }

    [Fact]
    public async Task The_write_routes_return_the_same_bodies_with_no_history_field()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");

        var created = await CreateAsync(client, world);
        var id = IdOf(created);
        var sent = await JsonOf(await client.PostAsync($"/api/invoices/{id}/send", null));

        foreach (var body in new[] { created, sent })
        {
            Assert.False(body.TryGetProperty("history", out _));
            Assert.False(body.TryGetProperty("entries", out _));
            Assert.False(body.TryGetProperty("actorEmail", out _));
        }
    }
}
