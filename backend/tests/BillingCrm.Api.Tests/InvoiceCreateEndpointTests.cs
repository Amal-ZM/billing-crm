using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

public class InvoiceCreateEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>A due date safely in the future for the real clock the API factory uses.</summary>
    private static string Due(int daysFromToday = 30) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Seeded(ApiFactory Factory, Customer Abc, Product Consulting);

    private static Seeded NewFactory()
    {
        var factory = new ApiFactory();
        var abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(abc);
        var consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(consulting);
        return new Seeded(factory, abc, consulting);
    }

    private static string Body(Guid? customer, string? due = null, string? notes = "Net 30", params string[] lines) =>
        "{" +
        (customer is { } c ? $"\"customerId\":\"{c}\"," : string.Empty) +
        $"\"dueDate\":\"{due ?? Due()}\"," +
        (notes is null ? string.Empty : $"\"notes\":{JsonSerializer.Serialize(notes)},") +
        "\"lines\":[" + string.Join(",", lines) + "]}";

    private static string NewLine(Guid product, string quantity = "2") => $"{{\"productId\":\"{product}\",\"quantity\":{quantity}}}";

    // ---- the acceptance scenario ----

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task Customer_ABC_and_CNS_001_with_quantity_2_is_created_as_a_Draft_with_one_line_of_quantity_2_for_either_role(string role)
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient(role);

        var response = await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/invoices/{id}", response.Headers.Location?.ToString());
        Assert.Equal("INV-000001", body.GetProperty("number").GetString());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal("ABC", body.GetProperty("customer").GetProperty("name").GetString());
        Assert.True(body.GetProperty("customer").GetProperty("isActive").GetBoolean());
        Assert.Equal("Net 30", body.GetProperty("notes").GetString());
        Assert.Equal(body.GetProperty("createdAtUtc").GetDateTime(), body.GetProperty("updatedAtUtc").GetDateTime());
        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(2m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal("Consulting hour", line.GetProperty("productName").GetString());
        Assert.Equal("CNS-001", line.GetProperty("sku").GetString());
        Assert.Equal("Hour", line.GetProperty("unit").GetString());
        Assert.Equal(100m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15m, line.GetProperty("vatPercent").GetDecimal());
        Assert.Single(factory.InvoiceStore.All);
    }

    [Fact]
    public async Task The_next_invoice_gets_the_next_number()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Agent");

        var first = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id)))));
        var second = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id)))));

        Assert.Equal("INV-000001", first.GetProperty("number").GetString());
        Assert.Equal("INV-000002", second.GetProperty("number").GetString());
    }

    [Fact]
    public async Task The_body_has_amounts_and_the_payment_figures_but_no_due_amount_or_balance_field()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id))));

        var names = new List<string>();
        void Collect(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        names.Add(property.Name);
                        Collect(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Collect(item);
                    }

                    break;
            }
        }

        Collect(await JsonOf(response));
        Assert.Contains("quantity", names);
        Assert.Contains("unitPrice", names);
        Assert.Contains("vatPercent", names);
        Assert.Contains("grandTotal", names);
        Assert.Contains("net", names);
        // Spec 008: paid, remaining, the payments list and acceptsPayment are part of the invoice (a new Draft has none paid).
        Assert.Contains("paid", names);
        Assert.Contains("remaining", names);
        Assert.Contains("payments", names);
        Assert.Contains("acceptsPayment", names);
        foreach (var word in new[] { "balance", "due amount" })
        {
            Assert.DoesNotContain(names, name => name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task The_saved_line_keeps_the_old_values_after_the_product_is_renamed_repriced_and_deactivated_through_the_products_api()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var agent = factory.CreateAuthorizedClient("Agent");
        using var admin = factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await agent.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id)))));
        var id = created.GetProperty("id").GetGuid();

        var edit = await admin.PutAsync(
            $"/api/products/{consulting.Id}",
            Json("""{"name":"Consulting day","sku":"CNS-002","unit":"Service","unitPrice":150,"vatPercent":0}"""));
        var off = await admin.PutAsync($"/api/products/{consulting.Id}/active", Json("""{"isActive":false}"""));
        var reopened = await JsonOf(await agent.GetAsync($"/api/invoices/{id}"));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var line = reopened.GetProperty("lines")[0];
        Assert.Equal("Consulting hour", line.GetProperty("productName").GetString());
        Assert.Equal("CNS-001", line.GetProperty("sku").GetString());
        Assert.Equal("Hour", line.GetProperty("unit").GetString());
        Assert.Equal(100m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15m, line.GetProperty("vatPercent").GetDecimal());
    }

    [Fact]
    public async Task Adding_the_same_product_again_later_uses_its_new_values_and_the_same_product_twice_is_two_lines()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Admin");
        var first = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id)))));
        await client.PutAsync($"/api/products/{consulting.Id}", Json("""{"name":"Consulting hour","sku":"CNS-001","unit":"Hour","unitPrice":150,"vatPercent":0}"""));

        var second = await JsonOf(await client.PostAsync(
            "/api/invoices",
            Json(Body(abc.Id, lines: [NewLine(consulting.Id, "1"), NewLine(consulting.Id, "3.5")]))));

        Assert.Equal(100m, first.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        var lines = second.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(150m, line.GetProperty("unitPrice").GetDecimal()));
        Assert.All(lines, line => Assert.Equal(0m, line.GetProperty("vatPercent").GetDecimal()));
        Assert.Equal([1m, 3.5m], lines.Select(line => line.GetProperty("quantity").GetDecimal()));
        Assert.Equal([1, 2], lines.Select(line => line.GetProperty("position").GetInt32()));
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1.50", 1.5)]
    [InlineData("0.01", 0.01)]
    [InlineData("999999.99", 999999.99)]
    public async Task Fractional_quantities_are_saved_exactly(string sent, double expected)
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id, sent))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((decimal)expected, (await JsonOf(response)).GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task A_free_product_can_be_added_and_arabic_text_round_trips()
    {
        var (factory, _, _) = NewFactory();
        await using var _f = factory;
        var customer = Customer.Create("شركة الأمل", CustomerType.Company, "a@amal.example", "0500000001", null, "الرياض", Now);
        factory.CustomerStore.Seed(customer);
        var free = Product.Create("عينة مجانية", "FREE-1", ProductUnit.Item, 0m, 0m, Now);
        factory.ProductStore.Seed(free);
        using var client = factory.CreateAuthorizedClient("Agent");

        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(customer.Id, notes: "ملاحظات\nالسطر الثاني", lines: NewLine(free.Id, "1")))));
        var fetched = await JsonOf(await client.GetAsync($"/api/invoices/{created.GetProperty("id").GetGuid()}"));

        Assert.Equal("شركة الأمل", fetched.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal("ملاحظات\nالسطر الثاني", fetched.GetProperty("notes").GetString());
        Assert.Equal("عينة مجانية", fetched.GetProperty("lines")[0].GetProperty("productName").GetString());
        Assert.Equal(0m, fetched.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task Notes_are_optional()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/invoices", Json(Body(abc.Id, notes: null, lines: NewLine(consulting.Id))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonOf(response)).GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task Status_number_and_other_properties_in_the_body_are_ignored()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Admin");
        var json = $"{{\"customerId\":\"{abc.Id}\",\"dueDate\":\"{Due()}\",\"status\":\"Paid\",\"number\":\"INV-999999\",\"isActive\":false," +
                   $"\"lines\":[{{\"productId\":\"{consulting.Id}\",\"quantity\":1,\"unitPrice\":1,\"productName\":\"Hacked\"}}]}}";

        var body = await JsonOf(await client.PostAsync("/api/invoices", Json(json)));

        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal("INV-000001", body.GetProperty("number").GetString());
        Assert.Equal(100m, body.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Consulting hour", body.GetProperty("lines")[0].GetProperty("productName").GetString());
    }

    // ---- refusals ----

    private async Task<(HttpResponseMessage Response, ApiFactory Factory)> PostAsync(Func<Seeded, string> body, Action<Seeded>? arrange = null)
    {
        var seeded = NewFactory();
        arrange?.Invoke(seeded);
        var client = seeded.Factory.CreateAuthorizedClient("Admin");
        var response = await client.PostAsync("/api/invoices", Json(body(seeded)));
        return (response, seeded.Factory);
    }

    [Fact]
    public async Task An_inactive_customer_is_a_400_naming_customerId_and_creates_nothing()
    {
        var (response, factory) = await PostAsync(
            s => Body(s.Abc.Id, lines: NewLine(s.Consulting.Id)),
            s => s.Abc.SetActive(false, Now));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Choose an active customer.", (await JsonOf(response)).GetProperty("errors").GetProperty("customerId")[0].GetString());
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public async Task An_unknown_malformed_or_missing_customer_is_a_400_naming_customerId(string kind)
    {
        var (response, factory) = await PostAsync(s => kind switch
        {
            "unknown" => Body(Guid.NewGuid(), lines: NewLine(s.Consulting.Id)),
            "malformed" => $"{{\"customerId\":\"nope\",\"dueDate\":\"{Due()}\",\"lines\":[{NewLine(s.Consulting.Id)}]}}",
            _ => Body(null, lines: NewLine(s.Consulting.Id)),
        });
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("customerId", out _));
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Fact]
    public async Task An_inactive_product_is_a_400_naming_the_line_and_creates_nothing()
    {
        var (response, factory) = await PostAsync(
            s => Body(s.Abc.Id, lines: [NewLine(s.Consulting.Id), NewLine(s.Consulting.Id)]),
            s => s.Consulting.SetActive(false, Now));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lines[0].productId", out _));
        Assert.True(errors.TryGetProperty("lines[1].productId", out _));
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public async Task An_unknown_malformed_or_missing_product_is_a_400_naming_the_line(string kind)
    {
        var (response, factory) = await PostAsync(s =>
        {
            var line = kind switch
            {
                "unknown" => NewLine(Guid.NewGuid()),
                "malformed" => "{\"productId\":\"nope\",\"quantity\":1}",
                _ => "{\"quantity\":1}",
            };
            return Body(s.Abc.Id, lines: line);
        });
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("lines[0].productId", out _));
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1.005")]
    [InlineData("1000000")]
    [InlineData("null")]
    public async Task Each_quantity_rule_is_a_400_naming_the_line_quantity_and_creates_nothing(string quantity)
    {
        var (response, factory) = await PostAsync(s => Body(s.Abc.Id, lines: NewLine(s.Consulting.Id, quantity)));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = (await JsonOf(response)).GetProperty("errors").GetProperty("lines[0].quantity")[0].GetString();
        Assert.StartsWith("Quantity", message);
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    [InlineData("past")]
    public async Task A_missing_invalid_or_past_due_date_is_a_400_naming_dueDate(string kind)
    {
        var (response, factory) = await PostAsync(s =>
        {
            var due = kind switch
            {
                "missing" => string.Empty,
                "garbage" => "31/12/2099",
                _ => "2000-01-01",
            };
            return kind == "missing"
                ? $"{{\"customerId\":\"{s.Abc.Id}\",\"lines\":[{NewLine(s.Consulting.Id)}]}}"
                : Body(s.Abc.Id, due: due, lines: NewLine(s.Consulting.Id));
        });
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("dueDate", out _));
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Fact]
    public async Task Today_is_accepted_as_a_due_date()
    {
        var saudiToday = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
        var (response, factory) = await PostAsync(s => Body(s.Abc.Id, due: saudiToday, lines: NewLine(s.Consulting.Id)));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Notes_over_1000_characters_are_a_400_naming_notes()
    {
        var (response, factory) = await PostAsync(s => Body(s.Abc.Id, notes: new string('n', 1001), lines: NewLine(s.Consulting.Id)));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("notes", out _));
        Assert.Empty(factory.InvoiceStore.All);
    }

    [Fact]
    public async Task No_lines_and_more_than_100_lines_are_a_400_naming_lines()
    {
        var (none, factoryA) = await PostAsync(s => Body(s.Abc.Id));
        await using var _a = factoryA;
        var (many, factoryB) = await PostAsync(s => Body(s.Abc.Id, lines: Enumerable.Range(0, 101).Select(_ => NewLine(s.Consulting.Id)).ToArray()));
        await using var _b = factoryB;

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.True((await JsonOf(none)).GetProperty("errors").TryGetProperty("lines", out _));
        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
        Assert.True((await JsonOf(many)).GetProperty("errors").TryGetProperty("lines", out _));
        Assert.Empty(factoryA.InvoiceStore.All);
        Assert.Empty(factoryB.InvoiceStore.All);
    }

    [Fact]
    public async Task Exactly_100_lines_are_accepted()
    {
        var (response, factory) = await PostAsync(s => Body(s.Abc.Id, lines: Enumerable.Range(0, 100).Select(_ => NewLine(s.Consulting.Id)).ToArray()));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(100, (await JsonOf(response)).GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task A_line_id_on_a_new_invoice_is_a_400_naming_the_line_id()
    {
        var (response, factory) = await PostAsync(s =>
            Body(s.Abc.Id, lines: $"{{\"id\":\"{Guid.NewGuid()}\",\"quantity\":1}}"));
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("lines[0].id", out _));
    }

    [Fact]
    public async Task Several_problems_are_all_named_with_array_paths()
    {
        var (response, factory) = await PostAsync(s => Body(
            Guid.NewGuid(),
            due: "2000-01-01",
            lines: [NewLine(s.Consulting.Id, "0"), NewLine(Guid.NewGuid())]));
        await using var factoryScope = factory;

        var errors = (await JsonOf(response)).GetProperty("errors");
        foreach (var key in new[] { "customerId", "dueDate", "lines[0].quantity", "lines[1].productId" })
        {
            Assert.True(errors.TryGetProperty(key, out _), key);
        }

        Assert.Empty(factory.InvoiceStore.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"customerId":"x","dueDate":"2099-01-01","lines":[{"productId":"y","quantity":"two"}]}""")]
    [InlineData("""{"customerId":5,"dueDate":"2099-01-01","lines":[]}""")]
    [InlineData("""{"customerId":"x","dueDate":20990101,"lines":[]}""")]
    public async Task An_empty_or_malformed_body_or_a_wrongly_typed_value_is_a_400_problem_details(string body)
    {
        var (response, factory) = await PostAsync(_ => body);
        await using var factoryScope = factory;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(factory.InvoiceStore.All);
    }

    // ---- get by id ----

    [Fact]
    public async Task Get_returns_the_invoice_with_the_customers_current_name_and_state()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var created = await JsonOf(await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id)))));
        abc.SetActive(false, Now.AddDays(1));

        var response = await client.GetAsync($"/api/invoices/{created.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("INV-000001", body.GetProperty("number").GetString());
        Assert.False(body.GetProperty("customer").GetProperty("isActive").GetBoolean());
        Assert.Single(body.GetProperty("lines").EnumerateArray());
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2b9c1e-1111-4222-8333-444455556666")]
    [InlineData("not-a-guid")]
    public async Task Get_of_an_unknown_or_malformed_id_is_a_404_problem_details(string id)
    {
        var (factory, _, _) = NewFactory();
        await using var _f = factory;
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync($"/api/invoices/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Invoice not found", (await JsonOf(response)).GetProperty("title").GetString());
    }

    // ---- access ----

    [Fact]
    public async Task Every_operation_without_a_token_is_a_401_and_creates_nothing()
    {
        var (factory, abc, consulting) = NewFactory();
        await using var factoryScope = factory;
        using var client = factory.CreateClient();

        var post = await client.PostAsync("/api/invoices", Json(Body(abc.Id, lines: NewLine(consulting.Id))));
        var one = await client.GetAsync($"/api/invoices/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, one.StatusCode);
        Assert.Empty(factory.InvoiceStore.All);
    }
}
