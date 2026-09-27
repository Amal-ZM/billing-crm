using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BillingCrm.Api.Tests;

public class CustomerUpdateEndpointTests
{
    private static object Body(
        string? name = "Acme Trading",
        string? type = "Company",
        string? email = "accounts@acme.example",
        string? phone = "+966 11 555 0100",
        string? vatNumber = null,
        string? address = "12 King Fahd Rd") => new { name, type, email, phone, vatNumber, address };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> AddAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/customers", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Edit_returns_200_with_the_changed_fields()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());

        var response = await client.PutAsJsonAsync($"/api/customers/{id}", Body(name: "Acme Group", phone: "0511111111", vatNumber: "VAT-9", address: "New place"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Acme Group", body.GetProperty("name").GetString());
        Assert.Equal("0511111111", body.GetProperty("phone").GetString());
        Assert.Equal("VAT-9", body.GetProperty("vatNumber").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Theory]
    [InlineData("accounts@acme.example")]
    [InlineData("ACCOUNTS@ACME.EXAMPLE")]
    public async Task Saving_a_customer_with_its_own_email_is_a_200(string ownEmail)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());

        var response = await client.PutAsJsonAsync($"/api/customers/{id}", Body(name: "Renamed", email: ownEmail));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("other@acme.example")]
    [InlineData("OTHER@ACME.EXAMPLE")]
    [InlineData("  Other@Acme.Example ")]
    public async Task Taking_another_customers_email_is_a_409_and_the_customer_is_unchanged(string othersEmail)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        await AddAsync(client, Body(name: "Other Co", email: "other@acme.example"));
        var id = await AddAsync(client, Body());

        var response = await client.PutAsJsonAsync($"/api/customers/{id}", Body(name: "Changed", email: othersEmail));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await JsonOf(response);
        Assert.Equal("Email already in use", problem.GetProperty("title").GetString());
        Assert.Equal("Another customer already uses this email address.", problem.GetProperty("detail").GetString());

        var stored = await JsonOf(await client.GetAsync($"/api/customers/{id}"));
        Assert.Equal("Acme Trading", stored.GetProperty("name").GetString());
        Assert.Equal("accounts@acme.example", stored.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("type", "Partnership")]
    [InlineData("email", "not-an-email")]
    [InlineData("phone", "12")]
    [InlineData("address", null)]
    [InlineData("vatNumber", "VAT#1")]
    public async Task Each_validation_rule_on_edit_is_a_400_naming_the_field(string field, string? value)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());
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

        var response = await client.PutAsJsonAsync($"/api/customers/{id}", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public async Task Editing_an_unknown_or_malformed_id_is_a_404(string id)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PutAsJsonAsync($"/api/customers/{id}", Body());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Customer not found", (await JsonOf(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Deactivating_returns_200_and_the_customer_stays_in_the_default_list_marked_inactive()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());

        var response = await client.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await JsonOf(response)).GetProperty("isActive").GetBoolean());

        var list = await JsonOf(await client.GetAsync("/api/customers"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.False(list.GetProperty("items")[0].GetProperty("isActive").GetBoolean());

        var onlyInactive = await JsonOf(await client.GetAsync("/api/customers?active=false"));
        Assert.Equal(1, onlyInactive.GetProperty("totalCount").GetInt32());
        var onlyActive = await JsonOf(await client.GetAsync("/api/customers?active=true"));
        Assert.Equal(0, onlyActive.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Reactivating_makes_the_customer_active_again()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());
        await client.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = false });

        var response = await client.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = true });

        Assert.True((await JsonOf(response)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Setting_the_state_it_already_has_is_a_200_that_changes_nothing()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());
        var before = await JsonOf(await client.GetAsync($"/api/customers/{id}"));

        var response = await client.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before.GetProperty("updatedAtUtc").GetDateTime(), (await JsonOf(response)).GetProperty("updatedAtUtc").GetDateTime());
    }

    [Fact]
    public async Task A_deactivated_customers_email_still_refuses_a_new_customer()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());
        await client.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = false });

        var second = await client.PostAsJsonAsync("/api/customers", Body(name: "Newcomer"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isActive\":null}")]
    [InlineData("{\"isActive\":\"yes\"}")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task A_missing_or_non_boolean_is_active_is_a_400(string body)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());

        var response = await client.PutAsync($"/api/customers/{id}/active", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_missing_is_active_names_the_field()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        var id = await AddAsync(client, Body());

        var response = await client.PutAsync($"/api/customers/{id}/active", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("isActive", out _));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public async Task Changing_the_state_of_an_unknown_customer_is_a_404(string id)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task There_is_no_way_to_delete_a_customer()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");
        var id = await AddAsync(client, Body());

        var response = await client.DeleteAsync($"/api/customers/{id}");

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Every_write_without_a_token_is_a_401_and_changes_nothing()
    {
        await using var factory = new ApiFactory();
        using var authorized = factory.CreateAuthorizedClient();
        var id = await AddAsync(authorized, Body());
        using var anonymous = factory.CreateClient();

        var edit = await anonymous.PutAsJsonAsync($"/api/customers/{id}", Body(name: "Hacked"));
        var toggle = await anonymous.PutAsJsonAsync($"/api/customers/{id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, toggle.StatusCode);
        var stored = await JsonOf(await authorized.GetAsync($"/api/customers/{id}"));
        Assert.Equal("Acme Trading", stored.GetProperty("name").GetString());
        Assert.True(stored.GetProperty("isActive").GetBoolean());
    }
}
