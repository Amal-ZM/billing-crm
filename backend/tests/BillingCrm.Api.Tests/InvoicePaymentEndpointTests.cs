using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>Recording payments over HTTP (spec 008, contracts/payments-api.md).</summary>
public class InvoicePaymentEndpointTests
{
    // The API uses the real clock, so invoices are sent "now" and payments are dated with the Saudi date of today.
    private static readonly DateTime Now = DateTime.UtcNow;

    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd");

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

    /// <summary>Puts an invoice (ABC, consulting x 2, 230.00) straight into the in-memory store in the given status.</summary>
    private static async Task<Invoice> SeedAsync(World world, InvoiceStatus status, string? dueDate = null)
    {
        var invoice = Invoice.Create(world.Abc.Id, DateOnly.Parse(dueDate ?? Day(30)), "Net 30", Now);
        invoice.AddLine(world.Consulting, 2m, Now);
        switch (status)
        {
            case InvoiceStatus.Sent:
                invoice.Send(Now);
                break;
            case InvoiceStatus.Paid:
                invoice.Send(Now);
                invoice.RecordPayment(DateOnly.Parse(Today()), 230.00m, PaymentMethod.Bank, null, Now);
                break;
            case InvoiceStatus.Cancelled:
                invoice.Cancel(Now);
                break;
        }

        await world.Factory.InvoiceStore.AddAsync(invoice, default);
        return invoice;
    }

    private static Task<HttpResponseMessage> Pay(HttpClient client, object id, string body) =>
        client.PostAsync($"/api/invoices/{id}/payments", Json(body));

    private static string Body(string amount, string method = "Cash", string? note = null, string? date = null) =>
        $"{{\"date\":\"{date ?? Today()}\",\"amount\":{amount},\"method\":\"{method}\"" +
        (note is null ? string.Empty : $",\"note\":\"{note}\"") + "}";

    // ---- success ----

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task Either_role_can_record_a_payment_and_gets_201_the_location_and_the_updated_invoice(string role)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient(role);
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, Body("100.00", "Cash", "First half"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/invoices/{invoice.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        var body = await JsonOf(response);
        Assert.Equal(invoice.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Sent", body.GetProperty("status").GetString());
        Assert.Equal(100.00m, body.GetProperty("paid").GetDecimal());
        Assert.Equal(130.00m, body.GetProperty("remaining").GetDecimal());
        Assert.True(body.GetProperty("acceptsPayment").GetBoolean());
        var payment = Assert.Single(body.GetProperty("payments").EnumerateArray());
        Assert.Equal(Today(), payment.GetProperty("date").GetString());
        Assert.Equal(100.00m, payment.GetProperty("amount").GetDecimal());
        Assert.Equal("Cash", payment.GetProperty("method").GetString());
        Assert.Equal("First half", payment.GetProperty("note").GetString());
        Assert.NotEqual(Guid.Empty, payment.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_worked_example_pays_100_then_130_on_230_and_ends_Paid_with_nothing_remaining()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var first = await JsonOf(await Pay(client, invoice.Id, Body("100.00")));
        var second = await JsonOf(await Pay(client, invoice.Id, Body("130.00", "Bank")));

        Assert.Equal(130.00m, first.GetProperty("remaining").GetDecimal());
        Assert.Equal("Paid", second.GetProperty("status").GetString());
        Assert.Equal(230.00m, second.GetProperty("paid").GetDecimal());
        Assert.Equal(0.00m, second.GetProperty("remaining").GetDecimal());
        Assert.False(second.GetProperty("acceptsPayment").GetBoolean());
        Assert.False(second.GetProperty("isOverdue").GetBoolean());
        Assert.Empty(second.GetProperty("allowedTransitions").EnumerateArray());
        Assert.Equal(2, second.GetProperty("payments").GetArrayLength());
        var reread = await JsonOf(await client.GetAsync($"/api/invoices/{invoice.Id}"));
        Assert.Equal("Paid", reread.GetProperty("status").GetString());
        var row = (await JsonOf(await client.GetAsync("/api/invoices"))).GetProperty("items")[0];
        Assert.Equal("Paid", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_amount_typed_with_a_trailing_zero_keeps_its_exact_value()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var body = await JsonOf(await Pay(client, invoice.Id, Body("100.10")));

        Assert.Equal(100.10m, body.GetProperty("payments")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(129.90m, body.GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task A_late_Sent_invoice_takes_a_payment_and_settled_it_is_no_longer_overdue()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent, dueDate: Day(-5));

        var part = await JsonOf(await Pay(client, invoice.Id, Body("100.00")));
        Assert.True(part.GetProperty("isOverdue").GetBoolean());
        Assert.True(part.GetProperty("acceptsPayment").GetBoolean());

        var settled = await JsonOf(await Pay(client, invoice.Id, Body("130.00")));
        Assert.Equal("Paid", settled.GetProperty("status").GetString());
        Assert.False(settled.GetProperty("isOverdue").GetBoolean());
    }

    [Fact]
    public async Task A_repriced_product_leaves_the_total_and_what_remains_as_read_back_over_HTTP()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var agent = world.Factory.CreateAuthorizedClient("Agent");
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        await Pay(agent, invoice.Id, Body("100.00"));

        var edit = await admin.PutAsync(
            $"/api/products/{world.Consulting.Id}",
            Json("""{"name":"Consulting hour","sku":"CNS-001","unit":"Hour","unitPrice":999.99,"vatPercent":0}"""));
        var reread = await JsonOf(await agent.GetAsync($"/api/invoices/{invoice.Id}"));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(230.00m, reread.GetProperty("totals").GetProperty("grandTotal").GetDecimal());
        Assert.Equal(130.00m, reread.GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task The_body_has_no_row_version_and_a_status_paid_or_id_in_the_request_is_ignored()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        var sneaky = $"{{\"date\":\"{Today()}\",\"amount\":10.00,\"method\":\"Cash\",\"status\":\"Paid\",\"paid\":230.00,\"remaining\":0,\"invoiceId\":\"{Guid.NewGuid()}\",\"id\":\"{Guid.NewGuid()}\"}}";

        var response = await Pay(client, invoice.Id, sneaky);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Sent", body.GetProperty("status").GetString());
        Assert.Equal(10.00m, body.GetProperty("paid").GetDecimal());
        Assert.Equal(invoice.Id, invoice.Payments.Single().InvoiceId);
        Assert.DoesNotContain("rowversion", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- not found and signed out ----

    [Fact]
    public async Task An_unknown_invoice_is_404()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");

        var response = await Pay(client, Guid.NewGuid(), Body("10.00"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Invoice not found", (await JsonOf(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_is_404_too()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");

        var response = await Pay(client, "not-a-guid", Body("10.00"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_the_route_is_401_and_nothing_is_recorded()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var anonymous = world.Factory.CreateClient();
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(anonymous, invoice.Id, Body("10.00"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(invoice.Payments);
    }

    // ---- refusals (US2) ----

    [Theory]
    [InlineData(InvoiceStatus.Draft, "Draft")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    public async Task A_payment_on_a_Draft_Cancelled_or_Paid_invoice_is_409_naming_the_status_and_changes_nothing(InvoiceStatus status, string shown)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, status);
        var paymentsBefore = invoice.Payments.Count;

        var response = await Pay(client, invoice.Id, Body("10.00"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await JsonOf(response);
        Assert.Equal("Payment not allowed", problem.GetProperty("title").GetString());
        Assert.Equal($"An invoice that is {shown} cannot take a payment.", problem.GetProperty("detail").GetString());
        Assert.Equal(paymentsBefore, invoice.Payments.Count);
        Assert.Equal(status, invoice.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task The_status_is_judged_before_the_values_of_a_readable_body_so_an_invalid_body_on_such_an_invoice_is_still_409(InvoiceStatus status)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, status);

        var response = await Pay(client, invoice.Id, "{\"date\":\"nope\",\"amount\":-1,\"method\":\"Card\"}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task A_body_the_framework_cannot_read_is_a_plain_400_even_on_an_invoice_that_could_not_take_a_payment_the_documented_carve_out(InvoiceStatus status)
    {
        // Pins the carve-out in the spec (scenario 10) and the contract: binding happens before the handler runs, so a body that is not
        // even JSON (or has text where a number belongs) is refused before the status is looked at, exactly as for 007's PUT.
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, status);

        var notJson = await Pay(client, invoice.Id, "{ this is not json");
        var textForNumber = await Pay(client, invoice.Id, "{\"date\":\"2030-06-01\",\"amount\":\"abc\",\"method\":\"Cash\"}");

        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, textForNumber.StatusCode);
        Assert.Equal(status, invoice.Status);
    }

    [Fact]
    public async Task Field_problems_are_400_reported_together_keyed_by_field_and_nothing_is_saved()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, $"{{\"date\":\"2030-02-30\",\"amount\":10.005,\"method\":\"cash\",\"note\":\"{new string('x', 1001)}\"}}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.Equal(["amount", "date", "method", "note"], errors.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(invoice.Payments);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public async Task One_halala_more_than_what_remains_is_400_on_the_amount_and_the_exact_remainder_settles()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        await Pay(client, invoice.Id, Body("100.00"));

        var over = await Pay(client, invoice.Id, Body("130.01"));
        var exact = await JsonOf(await Pay(client, invoice.Id, Body("130.00")));

        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Equal(
            "Amount cannot be more than the remaining 130.00 SAR.",
            (await JsonOf(over)).GetProperty("errors").GetProperty("amount")[0].GetString());
        Assert.Equal("Paid", exact.GetProperty("status").GetString());
        Assert.Equal(2, invoice.Payments.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.005")]
    public async Task A_zero_negative_or_three_decimal_amount_is_400_on_the_amount(string amount)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, Body(amount));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("amount", out _));
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public async Task A_missing_amount_a_missing_date_and_a_missing_method_are_each_400_on_their_field()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.Equal(["amount", "date", "method"], errors.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Tomorrow_and_the_day_before_the_invoice_was_sent_are_400_on_the_date_and_today_is_accepted()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        var tomorrow = DateOnly.Parse(Today()).AddDays(1).ToString("yyyy-MM-dd");
        var beforeSent = DateOnly.Parse(Today()).AddDays(-2).ToString("yyyy-MM-dd");

        var future = await Pay(client, invoice.Id, Body("10.00", date: tomorrow));
        var early = await Pay(client, invoice.Id, Body("10.00", date: beforeSent));
        var today = await Pay(client, invoice.Id, Body("10.00", date: Today()));

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal("Date cannot be in the future.", (await JsonOf(future)).GetProperty("errors").GetProperty("date")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.StartsWith("Date cannot be before the invoice was sent", (await JsonOf(early)).GetProperty("errors").GetProperty("date")[0].GetString());
        Assert.Equal(HttpStatusCode.Created, today.StatusCode);
    }

    [Fact]
    public async Task An_unreadable_body_is_a_400_and_saves_nothing()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await client.PostAsync($"/api/invoices/{invoice.Id}/payments", Json("{ this is not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(invoice.Payments);
    }

    [Theory]
    [InlineData("{\"date\":\"TODAY\",\"amount\":\"100\",\"method\":\"Cash\"}")] // a string, not a number
    [InlineData("{\"date\":\"TODAY\",\"amount\":1e400,\"method\":\"Cash\"}")] // beyond any decimal
    [InlineData("{\"date\":20300601,\"amount\":10,\"method\":\"Cash\"}")] // a number where text is expected
    [InlineData("")] // an empty body
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Odd_bodies_are_a_400_never_a_500_and_record_nothing(string body)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, body.Replace("TODAY", Today()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(invoice.Payments);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public async Task A_number_with_more_than_28_significant_digits_is_now_refused_closing_the_spec_012_gap()
    {
        // Used to document a gap: before spec 012, 100.000...0001 (33 digits) was read as exactly 100, so the third-decimal rule
        // never saw the tail (silent data loss on a financial field). DecimalJsonConverter now refuses it outright instead.
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, Body("100.0000000000000000000000000000001"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public async Task Exponent_notation_is_now_refused_closing_the_spec_012_gap()
    {
        // Used to document that exponent notation was silently accepted as the plain number it denotes. DecimalJsonConverter now
        // refuses it outright instead (spec 012, FR-010).
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await Pay(client, invoice.Id, Body("1E2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_refused_and_records_nothing()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);

        var response = await client.PostAsync(
            $"/api/invoices/{invoice.Id}/payments",
            new StringContent($"date={Today()}&amount=10&method=Cash", Encoding.UTF8, "text/plain"));

        Assert.True(response.StatusCode is HttpStatusCode.UnsupportedMediaType or HttpStatusCode.BadRequest, response.StatusCode.ToString());
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public async Task Every_refusal_is_problem_details_and_leaves_the_invoice_as_it_was()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        await Pay(client, invoice.Id, Body("100.00"));

        foreach (var body in new[] { Body("0"), Body("500.00"), Body("10.00", "Card"), "{}" })
        {
            var response = await Pay(client, invoice.Id, body);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        var reread = await JsonOf(await client.GetAsync($"/api/invoices/{invoice.Id}"));
        Assert.Equal(100.00m, reread.GetProperty("paid").GetDecimal());
        Assert.Equal(130.00m, reread.GetProperty("remaining").GetDecimal());
        Assert.Equal("Sent", reread.GetProperty("status").GetString());
        Assert.Equal(1, reread.GetProperty("payments").GetArrayLength());
    }

    // ---- cancel once there is a payment ----

    [Fact]
    public async Task An_Admin_cancelling_a_Sent_invoice_that_has_a_payment_gets_409_has_payments_and_nothing_changes()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var agent = world.Factory.CreateAuthorizedClient("Agent");
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        await Pay(agent, invoice.Id, Body("100.00"));

        var response = await admin.PostAsync($"/api/invoices/{invoice.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await JsonOf(response);
        Assert.Equal("Invoice has payments", problem.GetProperty("title").GetString());
        Assert.Equal("An invoice that has payments cannot be cancelled.", problem.GetProperty("detail").GetString());
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Null(invoice.CancelledAtUtc);
        var reread = await JsonOf(await admin.GetAsync($"/api/invoices/{invoice.Id}"));
        Assert.Empty(reread.GetProperty("allowedTransitions").EnumerateArray());
    }

    [Fact]
    public async Task An_Agent_cancelling_gets_403_first_also_for_an_invoice_with_payments_and_for_an_unknown_id()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var agent = world.Factory.CreateAuthorizedClient("Agent");
        var invoice = await SeedAsync(world, InvoiceStatus.Sent);
        await Pay(agent, invoice.Id, Body("100.00"));

        var withPayments = await agent.PostAsync($"/api/invoices/{invoice.Id}/cancel", null);
        var unknown = await agent.PostAsync($"/api/invoices/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.Forbidden, withPayments.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public async Task An_Admin_can_still_cancel_a_Sent_invoice_with_no_payment_and_a_Paid_one_is_still_the_illegal_change()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var admin = world.Factory.CreateAuthorizedClient("Admin");
        var noPayment = await SeedAsync(world, InvoiceStatus.Sent);
        var paid = await SeedAsync(world, InvoiceStatus.Paid);

        var cancelled = await admin.PostAsync($"/api/invoices/{noPayment.Id}/cancel", null);
        var refused = await admin.PostAsync($"/api/invoices/{paid.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await JsonOf(cancelled)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("An invoice that is Paid cannot be cancelled.", (await JsonOf(refused)).GetProperty("detail").GetString());
    }
}
