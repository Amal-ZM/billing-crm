using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>Sending, cancelling and the Overdue view over HTTP (spec 007, contracts/invoice-status-api.md).</summary>
public class InvoiceStatusEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Day(int daysFromToday) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

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

    /// <summary>Puts an invoice (ABC, consulting x 2, 230.00) straight into the in-memory store in the given status and due date.</summary>
    private static async Task<Invoice> SeedAsync(World world, InvoiceStatus status, string dueDate)
    {
        var invoice = Invoice.Create(world.Abc.Id, DateOnly.Parse(dueDate), "Net 30", Now);
        invoice.AddLine(world.Consulting, 2m, Now);
        switch (status)
        {
            case InvoiceStatus.Sent:
                invoice.Send(Now);
                break;
            case InvoiceStatus.Paid:
                invoice.Send(Now);
                typeof(Invoice).GetProperty(nameof(Invoice.Status))!.SetValue(invoice, InvoiceStatus.Paid);
                break;
            case InvoiceStatus.Cancelled:
                invoice.Cancel(Now);
                break;
        }

        await world.Factory.InvoiceStore.AddAsync(invoice, default);
        return invoice;
    }

    private static async Task<JsonElement> CreateDraftAsync(HttpClient client, World world)
    {
        var body = $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"Net 30\",\"lines\":[{{\"productId\":\"{world.Consulting.Id}\",\"quantity\":2}}]}}";
        var response = await client.PostAsync("/api/invoices", Json(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonOf(response);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, object id) => client.PostAsync($"/api/invoices/{id}/send", null);

    private static Task<HttpResponseMessage> Cancel(HttpClient client, object id) => client.PostAsync($"/api/invoices/{id}/cancel", null);

    // ---- the new fields on every invoice body ----

    [Fact]
    public async Task A_new_Draft_carries_the_status_fields_and_the_list_rows_carry_isOverdue()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");

        var created = await CreateDraftAsync(client, world);

        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.False(created.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("sentAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("cancelledAtUtc").ValueKind);
        Assert.Equal(["Sent", "Cancelled"], created.GetProperty("allowedTransitions").EnumerateArray().Select(item => item.GetString()));
        Assert.True(created.GetProperty("isEditable").GetBoolean());
        var row = (await JsonOf(await client.GetAsync("/api/invoices"))).GetProperty("items")[0];
        Assert.False(row.GetProperty("isOverdue").GetBoolean());
        Assert.Equal("Draft", row.GetProperty("status").GetString());
        Assert.False(row.TryGetProperty("rowVersion", out _));
    }

    [Fact]
    public async Task No_body_ever_carries_the_row_version()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var created = await CreateDraftAsync(client, world);

        var sent = await JsonOf(await Send(client, created.GetProperty("id").GetGuid()));

        Assert.DoesNotContain("rowversion", sent.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rowVersion", created.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- send ----

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task Either_role_can_send_a_Draft_and_the_body_shows_Sent_the_time_and_the_lock(string role)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient(role);
        var created = await CreateDraftAsync(client, world);

        var response = await Send(client, created.GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Sent", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("sentAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cancelledAtUtc").ValueKind);
        Assert.False(body.GetProperty("isEditable").GetBoolean());
        Assert.False(body.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(["Cancelled"], body.GetProperty("allowedTransitions").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(created.GetProperty("number").GetString(), body.GetProperty("number").GetString());
        Assert.Equal(230.00m, body.GetProperty("totals").GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task Sending_twice_is_a_409_that_names_the_current_status()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var id = (await CreateDraftAsync(client, world)).GetProperty("id").GetGuid();
        await Send(client, id);

        var response = await Send(client, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await JsonOf(response);
        Assert.Equal("Illegal status change", body.GetProperty("title").GetString());
        Assert.Equal("An invoice that is Sent cannot be sent.", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public async Task Sending_a_Paid_or_Cancelled_invoice_is_a_409(InvoiceStatus status, string shown)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, status, Day(30));

        var response = await Send(client, invoice.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"An invoice that is {shown} cannot be sent.", (await JsonOf(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Sending_an_Overdue_invoice_names_Overdue_in_the_409()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent, Day(-3));

        var response = await Send(client, invoice.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("An invoice that is Overdue cannot be sent.", (await JsonOf(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_Draft_with_a_past_due_date_is_a_400_on_dueDate_and_stays_a_Draft()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var draft = await SeedAsync(world, InvoiceStatus.Draft, Day(-3));

        var response = await Send(client, draft.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.Equal("Due date is in the past. Change the due date before sending.", errors.GetProperty("dueDate")[0].GetString());
        Assert.Equal("Draft", (await JsonOf(await client.GetAsync($"/api/invoices/{draft.Id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sending_an_unknown_or_malformed_id_is_a_404_and_no_token_is_a_401()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        using var anonymous = world.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, "not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task A_send_that_lost_a_race_is_a_409_invoice_changed()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var id = (await CreateDraftAsync(client, world)).GetProperty("id").GetGuid();
        world.Factory.InvoiceStore.SaveLosesRace = true;

        var response = await Send(client, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Invoice changed", (await JsonOf(response)).GetProperty("title").GetString());
    }

    // ---- a locked invoice cannot be edited ----

    [Fact]
    public async Task Editing_a_Sent_invoice_is_a_409_with_any_body_and_the_invoice_is_unchanged()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var created = await CreateDraftAsync(client, world);
        var id = created.GetProperty("id").GetGuid();
        await Send(client, id);
        var before = await client.GetStringAsync($"/api/invoices/{id}");
        var lineId = created.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var valid = await client.PutAsync(
            $"/api/invoices/{id}",
            Json($"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(40)}\",\"notes\":\"x\",\"lines\":[{{\"id\":\"{lineId}\",\"quantity\":9}}]}}"));
        var invalid = await client.PutAsync($"/api/invoices/{id}", Json("{}"));

        foreach (var response in new[] { valid, invalid })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await JsonOf(response);
            Assert.Equal("Invoice is locked", body.GetProperty("title").GetString());
            Assert.Equal("Only a Draft invoice can be edited. This invoice is Sent.", body.GetProperty("detail").GetString());
        }

        Assert.Equal(before, await client.GetStringAsync($"/api/invoices/{id}"));
    }

    [Fact]
    public async Task A_body_that_tries_to_set_the_status_or_the_row_version_on_an_edit_is_ignored()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var created = await CreateDraftAsync(client, world);
        var id = created.GetProperty("id").GetGuid();
        var lineId = created.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var response = await client.PutAsync(
            $"/api/invoices/{id}",
            Json(
                $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"x\",\"status\":\"Cancelled\",\"sentAtUtc\":\"2030-01-01T00:00:00Z\"," +
                $"\"cancelledAtUtc\":\"2030-01-01T00:00:00Z\",\"rowVersion\":\"AAAA\",\"frozenGrandTotal\":1,\"lines\":[{{\"id\":\"{lineId}\",\"quantity\":3}}]}}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sentAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cancelledAtUtc").ValueKind);
        Assert.Equal(345.00m, body.GetProperty("totals").GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task An_unreadable_body_on_a_locked_invoice_is_refused_by_the_framework_with_a_400_and_nothing_changes()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var id = (await CreateDraftAsync(client, world)).GetProperty("id").GetGuid();
        await Send(client, id);
        var before = await client.GetStringAsync($"/api/invoices/{id}");

        var response = await client.PutAsync($"/api/invoices/{id}", Json("{ not json"));

        // A body that cannot even be read never reaches the service (as in 005), so it is a 400, not the 409 a readable body gets.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await client.GetStringAsync($"/api/invoices/{id}"));
    }

    // ---- cancel (Admin only) ----

    [Theory]
    [InlineData(InvoiceStatus.Draft, 30)]
    [InlineData(InvoiceStatus.Sent, 30)]
    [InlineData(InvoiceStatus.Sent, -3)]   // Overdue
    public async Task An_Admin_cancels_a_Draft_a_Sent_and_an_Overdue_invoice_and_its_number_lines_and_amounts_are_kept(InvoiceStatus status, int dueInDays)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, status, Day(dueInDays));

        var response = await Cancel(admin, invoice.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("cancelledAtUtc").ValueKind);
        Assert.Equal(status == InvoiceStatus.Sent ? JsonValueKind.String : JsonValueKind.Null, body.GetProperty("sentAtUtc").ValueKind);
        Assert.False(body.GetProperty("isOverdue").GetBoolean());
        Assert.False(body.GetProperty("isEditable").GetBoolean());
        Assert.Empty(body.GetProperty("allowedTransitions").EnumerateArray());
        Assert.Equal(230.00m, body.GetProperty("totals").GetProperty("grandTotal").GetDecimal());
        Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(InvoiceNumber.Format(invoice.Number), body.GetProperty("number").GetString());
    }

    [Fact]
    public async Task An_Agent_gets_403_on_cancel_for_a_Draft_a_Sent_a_Cancelled_and_an_unknown_or_malformed_id_and_nothing_changes()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var agent = world.Factory.CreateAuthorizedClient("Agent");
        var draft = await SeedAsync(world, InvoiceStatus.Draft, Day(30));
        var sent = await SeedAsync(world, InvoiceStatus.Sent, Day(30));
        var cancelled = await SeedAsync(world, InvoiceStatus.Cancelled, Day(30));

        foreach (var id in new object[] { draft.Id, sent.Id, cancelled.Id, Guid.NewGuid(), "not-a-guid" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Cancel(agent, id)).StatusCode);
        }

        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Equal(InvoiceStatus.Sent, sent.Status);
        Assert.Equal(InvoiceStatus.Cancelled, cancelled.Status);
        Assert.Null(draft.CancelledAtUtc);
    }

    [Fact]
    public async Task Cancel_without_a_token_is_401()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var anonymous = world.Factory.CreateClient();
        var invoice = await SeedAsync(world, InvoiceStatus.Draft, Day(30));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cancel(anonymous, invoice.Id)).StatusCode);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public async Task Cancelling_a_Paid_or_Cancelled_invoice_is_a_409_naming_the_status(InvoiceStatus status, string shown)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, status, Day(30));

        var response = await Cancel(admin, invoice.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Illegal status change", body.GetProperty("title").GetString());
        Assert.Equal($"An invoice that is {shown} cannot be cancelled.", body.GetProperty("detail").GetString());
        Assert.Equal(status, invoice.Status);
    }

    [Fact]
    public async Task Cancelling_twice_is_a_409_and_an_unknown_id_is_a_404_for_an_Admin()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var draft = await SeedAsync(world, InvoiceStatus.Draft, Day(30));

        Assert.Equal(HttpStatusCode.OK, (await Cancel(admin, draft.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Cancel(admin, draft.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel(admin, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel(admin, "not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task A_Cancelled_invoice_stays_in_the_list_with_its_number_and_status_and_cannot_be_sent_or_edited()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, InvoiceStatus.Draft, Day(30));
        await Cancel(admin, invoice.Id);

        var row = (await JsonOf(await admin.GetAsync("/api/invoices"))).GetProperty("items")[0];
        var send = await Send(admin, invoice.Id);
        var edit = await admin.PutAsync($"/api/invoices/{invoice.Id}", Json("{}"));

        Assert.Equal("Cancelled", row.GetProperty("status").GetString());
        Assert.Equal(InvoiceNumber.Format(invoice.Number), row.GetProperty("number").GetString());
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    // ---- Overdue is derived (US3) ----

    [Theory]
    [InlineData(InvoiceStatus.Sent, 3, false)]     // due in three days
    [InlineData(InvoiceStatus.Sent, -3, true)]     // due three days ago
    [InlineData(InvoiceStatus.Draft, -3, false)]
    [InlineData(InvoiceStatus.Paid, -3, false)]
    [InlineData(InvoiceStatus.Cancelled, -3, false)]
    public async Task Get_and_the_list_report_isOverdue_only_for_a_Sent_invoice_past_its_due_date_and_the_status_stays_the_stored_one(InvoiceStatus status, int dueInDays, bool expected)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, status, Day(dueInDays));

        var opened = await JsonOf(await client.GetAsync($"/api/invoices/{invoice.Id}"));
        var listed = (await JsonOf(await client.GetAsync("/api/invoices"))).GetProperty("items")[0];

        Assert.Equal(expected, opened.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(expected, listed.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(status.ToString(), opened.GetProperty("status").GetString());
        Assert.Equal(status.ToString(), listed.GetProperty("status").GetString());
        Assert.DoesNotContain("\"Overdue\"", opened.GetRawText());
    }

    [Fact]
    public async Task An_Overdue_invoice_shows_the_same_amounts_and_follows_the_Sent_rules()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent, Day(-3));

        var opened = await JsonOf(await client.GetAsync($"/api/invoices/{invoice.Id}"));

        Assert.True(opened.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(230.00m, opened.GetProperty("totals").GetProperty("grandTotal").GetDecimal());
        Assert.Equal(["Cancelled"], opened.GetProperty("allowedTransitions").EnumerateArray().Select(item => item.GetString()));
        Assert.False(opened.GetProperty("isEditable").GetBoolean());
    }

    [Fact]
    public async Task A_cancel_that_lost_a_race_is_a_409_invoice_changed()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var draft = await SeedAsync(world, InvoiceStatus.Draft, Day(30));
        world.Factory.InvoiceStore.SaveLosesRace = true;

        var response = await Cancel(admin, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Invoice changed", (await JsonOf(response)).GetProperty("title").GetString());
    }
}
