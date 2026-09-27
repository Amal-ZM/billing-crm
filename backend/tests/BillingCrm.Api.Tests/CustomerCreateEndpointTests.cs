using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BillingCrm.Api.Tests;

public class CustomerCreateEndpointTests
{
    private static object Body(
        string? name = "Acme Trading",
        string? type = "Company",
        string? email = "accounts@acme.example",
        string? phone = "+966 11 555 0100",
        string? vatNumber = null,
        string? address = "12 King Fahd Rd, Riyadh") => new { name, type, email, phone, vatNumber, address };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task Add_returns_201_with_location_the_customer_and_an_active_state_for_either_role(string role)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient(role);

        var response = await client.PostAsJsonAsync("/api/customers", Body(vatNumber: "300000000000003"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/customers/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Acme Trading", body.GetProperty("name").GetString());
        Assert.Equal("Company", body.GetProperty("type").GetString());
        Assert.Equal("300000000000003", body.GetProperty("vatNumber").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(body.GetProperty("createdAtUtc").GetDateTime(), body.GetProperty("updatedAtUtc").GetDateTime());
    }

    [Fact]
    public async Task A_customer_without_a_vat_number_saves_with_null()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/customers", Body(vatNumber: ""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonOf(response)).GetProperty("vatNumber").ValueKind);
    }

    [Fact]
    public async Task A_new_customer_appears_in_the_list()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var created = await JsonOf(await client.PostAsJsonAsync("/api/customers", Body()));

        var list = await JsonOf(await client.GetAsync("/api/customers"));

        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(created.GetProperty("id").GetGuid(), list.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("accounts@acme.example")]
    [InlineData("ACCOUNTS@ACME.EXAMPLE")]
    [InlineData("  Accounts@Acme.Example  ")]
    public async Task A_duplicate_email_is_a_409_with_the_clear_message_and_creates_nothing(string duplicate)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        await client.PostAsJsonAsync("/api/customers", Body());

        var second = await client.PostAsJsonAsync("/api/customers", Body(name: "Someone Else", email: duplicate));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var body = await JsonOf(second);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("Email already in use", body.GetProperty("title").GetString());
        Assert.Equal("Another customer already uses this email address.", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("Acme", body.GetRawText());
        Assert.Single(factory.CustomerStore.All);
    }

    [Theory]
    [InlineData("name", null)]
    [InlineData("name", "")]
    [InlineData("type", null)]
    [InlineData("type", "Partnership")]
    [InlineData("email", null)]
    [InlineData("email", "not-an-email")]
    [InlineData("phone", null)]
    [InlineData("phone", "12")]
    [InlineData("phone", "0500-abc-1234")]
    [InlineData("address", null)]
    [InlineData("vatNumber", "VAT#1")]
    public async Task Each_validation_rule_is_a_400_naming_the_field(string field, string? value)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var payload = new Dictionary<string, string?>
        {
            ["name"] = "Acme Trading",
            ["type"] = "Company",
            ["email"] = "accounts@acme.example",
            ["phone"] = "+966 11 555 0100",
            ["vatNumber"] = null,
            ["address"] = "12 King Fahd Rd",
        };
        payload[field] = value;

        var response = await client.PostAsJsonAsync("/api/customers", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(factory.CustomerStore.All);
    }

    [Fact]
    public async Task Several_bad_fields_are_all_named()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/customers", Body(name: null, email: "bad", phone: "1"));

        var errors = (await JsonOf(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("phone", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task An_empty_or_malformed_body_is_a_400_problem_details(string body)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsync("/api/customers", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_client_cannot_create_an_inactive_customer_by_sending_isActive()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Acme", type = "Company", email = "a@acme.example", phone = "0500000000", address = "Somewhere", isActive = false,
        });

        Assert.True((await JsonOf(response)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Get_returns_the_customer()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var created = await JsonOf(await client.PostAsJsonAsync("/api/customers", Body()));
        var id = created.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/customers/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Acme Trading", (await JsonOf(response)).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2b9c1e-1111-4222-8333-444455556666")]
    [InlineData("not-a-guid")]
    public async Task Get_of_an_unknown_or_malformed_id_is_a_404_problem_details(string id)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.GetAsync($"/api/customers/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Customer not found", (await JsonOf(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Arabic_text_round_trips_unchanged()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var created = await JsonOf(await client.PostAsJsonAsync("/api/customers", Body(name: "شركة الأمل", address: "الرياض\nحي النخيل")));
        var fetched = await JsonOf(await client.GetAsync($"/api/customers/{created.GetProperty("id").GetGuid()}"));

        Assert.Equal("شركة الأمل", fetched.GetProperty("name").GetString());
        Assert.Equal("الرياض\nحي النخيل", fetched.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Every_operation_without_a_token_is_a_401()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/api/customers", Body());
        var list = await client.GetAsync("/api/customers");
        var one = await client.GetAsync($"/api/customers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, one.StatusCode);
        Assert.Empty(factory.CustomerStore.All);
    }
}
