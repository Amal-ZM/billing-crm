using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BillingCrm.Api.Tests;

public class ProductCreateEndpointTests
{
    private const string ValidJson = """{"name":"Consulting hour","sku":"CNS-001","unit":"Hour","unitPrice":100,"vatPercent":15}""";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Body(
        string name = "\"Consulting hour\"",
        string sku = "\"CNS-001\"",
        string unit = "\"Hour\"",
        string unitPrice = "100",
        string? vatPercent = "15")
    {
        var vat = vatPercent is null ? string.Empty : $",\"vatPercent\":{vatPercent}";
        return $"{{\"name\":{name},\"sku\":{sku},\"unit\":{unit},\"unitPrice\":{unitPrice}{vat}}}";
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task An_admin_adds_cns_001_with_price_100_and_vat_15_and_it_appears_in_the_list()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/products", Json(ValidJson));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/products/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Consulting hour", body.GetProperty("name").GetString());
        Assert.Equal("CNS-001", body.GetProperty("sku").GetString());
        Assert.Equal("Hour", body.GetProperty("unit").GetString());
        Assert.Equal(100m, body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15m, body.GetProperty("vatPercent").GetDecimal());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(body.GetProperty("createdAtUtc").GetDateTime(), body.GetProperty("updatedAtUtc").GetDateTime());

        var list = await JsonOf(await client.GetAsync("/api/products"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("CNS-001", item.GetProperty("sku").GetString());
        Assert.Equal(100m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15m, item.GetProperty("vatPercent").GetDecimal());
    }

    [Fact]
    public async Task The_sku_is_saved_trimmed_and_upper_case()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/products", Json(Body(sku: "\"  cns-001 \"")));

        Assert.Equal("CNS-001", (await JsonOf(response)).GetProperty("sku").GetString());
    }

    [Fact]
    public async Task A_missing_vat_is_saved_as_15_and_an_explicit_zero_is_kept()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var defaulted = await JsonOf(await client.PostAsync("/api/products", Json(Body(sku: "\"A-1\"", vatPercent: null))));
        var zero = await JsonOf(await client.PostAsync("/api/products", Json(Body(sku: "\"A-2\"", vatPercent: "0"))));

        Assert.Equal(15m, defaulted.GetProperty("vatPercent").GetDecimal());
        Assert.Equal(0m, zero.GetProperty("vatPercent").GetDecimal());
    }

    [Theory]
    [InlineData("100.5", "100.5")]
    [InlineData("100.50", "100.50")]
    [InlineData("0.10", "0.10")]
    [InlineData("999999999.99", "999999999.99")]
    [InlineData("0", "0")]
    public async Task Prices_are_stored_and_returned_exactly(string sent, string expected)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/products", Json(Body(unitPrice: sent)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            (await JsonOf(response)).GetProperty("unitPrice").GetDecimal());
    }

    [Theory]
    [InlineData("CNS-001")]
    [InlineData("cns-001")]
    [InlineData("  CNS-001  ")]
    public async Task A_duplicate_sku_is_a_409_with_the_clear_message_and_creates_nothing(string duplicate)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");
        await client.PostAsync("/api/products", Json(ValidJson));

        var second = await client.PostAsync("/api/products", Json(Body(name: "\"Someone Else\"", sku: $"\"{duplicate}\"")));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var body = await JsonOf(second);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("SKU already in use", body.GetProperty("title").GetString());
        Assert.Equal("Another product already uses this SKU.", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("Consulting", body.GetRawText());
        Assert.Single(factory.ProductStore.All);
    }

    [Theory]
    [InlineData("name", "null")]
    [InlineData("name", "\"\"")]
    [InlineData("sku", "null")]
    [InlineData("sku", "\"AB CD\"")]
    [InlineData("sku", "\"AB#1\"")]
    [InlineData("sku", "\"رمز\"")]
    [InlineData("unit", "null")]
    [InlineData("unit", "\"Box\"")]
    [InlineData("unitPrice", "null")]
    [InlineData("unitPrice", "-1")]
    [InlineData("unitPrice", "10.005")]
    [InlineData("unitPrice", "1000000000")]
    [InlineData("vatPercent", "100.01")]
    [InlineData("vatPercent", "-1")]
    [InlineData("vatPercent", "15.005")]
    public async Task Each_validation_rule_is_a_400_naming_the_field(string field, string value)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");
        var fields = new Dictionary<string, string>
        {
            ["name"] = "\"Consulting hour\"",
            ["sku"] = "\"CNS-001\"",
            ["unit"] = "\"Hour\"",
            ["unitPrice"] = "100",
            ["vatPercent"] = "15",
        };
        fields[field] = value;
        var json = "{" + string.Join(",", fields.Select(pair => $"\"{pair.Key}\":{pair.Value}")) + "}";

        var response = await client.PostAsync("/api/products", Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(factory.ProductStore.All);
    }

    [Fact]
    public async Task Several_bad_fields_are_all_named()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/products", Json(Body(name: "null", sku: "\"AB CD\"", unit: "\"Box\"", unitPrice: "-1", vatPercent: "101")));

        var errors = (await JsonOf(response)).GetProperty("errors");
        foreach (var field in new[] { "name", "sku", "unit", "unitPrice", "vatPercent" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"name":"X","sku":"X-1","unit":"Item","unitPrice":"abc"}""")]
    [InlineData("""{"name":"X","sku":"X-1","unit":"Item","unitPrice":1,"vatPercent":"lots"}""")]
    [InlineData("""{"name":"X","sku":"X-1","unit":1,"unitPrice":1}""")]
    [InlineData("""{"name":123,"sku":"X-1","unit":"Item","unitPrice":1}""")]
    [InlineData("""{"name":"X","sku":"X-1","unit":"Item","unitPrice":"100"}""")]
    [InlineData("""{"name":"X","sku":"X-1","unit":"Item","unitPrice":100,"vatPercent":"15"}""")]
    public async Task An_empty_or_malformed_body_or_a_wrongly_typed_value_is_a_400_problem_details(string body)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/products", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(factory.ProductStore.All);
    }

    [Fact]
    public async Task A_client_cannot_create_an_inactive_product_by_sending_is_active()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsync("/api/products",
            Json("""{"name":"X","sku":"X-1","unit":"Item","unitPrice":1,"isActive":false}"""));

        Assert.True((await JsonOf(response)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Get_returns_the_product()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await client.PostAsync("/api/products", Json(ValidJson)));
        var id = created.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/products/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("CNS-001", body.GetProperty("sku").GetString());
        Assert.Equal(100m, body.GetProperty("unitPrice").GetDecimal());
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2b9c1e-1111-4222-8333-444455556666")]
    [InlineData("not-a-guid")]
    public async Task Get_of_an_unknown_or_malformed_id_is_a_404_problem_details(string id)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync($"/api/products/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Product not found", (await JsonOf(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Arabic_names_round_trip_unchanged()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var created = await JsonOf(await client.PostAsync("/api/products", Json(Body(name: "\"ساعة استشارة\""))));
        var fetched = await JsonOf(await client.GetAsync($"/api/products/{created.GetProperty("id").GetGuid()}"));

        Assert.Equal("ساعة استشارة", fetched.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(ValidJson)]
    [InlineData("""{"name":"","sku":"AB CD","unit":"Box","unitPrice":-1}""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task An_agent_adding_a_product_gets_403_whatever_the_body_and_nothing_is_created(string body)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Agent");

        var response = await client.PostAsync("/api/products", Json(body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Forbidden", (await JsonOf(response)).GetProperty("title").GetString());
        Assert.Empty(factory.ProductStore.All);
    }

    [Fact]
    public async Task An_agent_can_list_and_open_a_product()
    {
        await using var factory = new ApiFactory();
        using var admin = factory.CreateAuthorizedClient("Admin");
        var created = await JsonOf(await admin.PostAsync("/api/products", Json(ValidJson)));
        using var agent = factory.CreateAuthorizedClient("Agent");

        var list = await agent.GetAsync("/api/products");
        var one = await agent.GetAsync($"/api/products/{created.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, (await JsonOf(list)).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
    }

    [Fact]
    public async Task Every_operation_without_a_token_is_a_401()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var post = await client.PostAsync("/api/products", Json(ValidJson));
        var list = await client.GetAsync("/api/products");
        var one = await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, one.StatusCode);
        Assert.Empty(factory.ProductStore.All);
    }
}
