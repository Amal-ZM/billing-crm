using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

public class ProductUpdateEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Body(string name = "Consulting hour", string sku = "CNS-001", string unit = "Hour", string price = "100", string? vat = "15")
    {
        var vatPart = vat is null ? string.Empty : $",\"vatPercent\":{vat}";
        return $"{{\"name\":\"{name}\",\"sku\":\"{sku}\",\"unit\":\"{unit}\",\"unitPrice\":{price}{vatPart}}}";
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static Product Seed(ApiFactory factory, string sku, string name, bool active = true)
    {
        var product = Product.Create(name, sku, ProductUnit.Hour, 100m, 15m, Now);
        if (!active)
        {
            product.SetActive(false, Now);
        }

        factory.ProductStore.Seed(product);
        return product;
    }

    private static async Task<List<string>> SkusOf(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/products{query}");
        return (await JsonOf(response)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sku").GetString()!).ToList();
    }

    [Fact]
    public async Task An_admin_edits_a_product_and_gets_the_new_values_back()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PutAsync($"/api/products/{product.Id}", Json(Body(name: "Consulting day", sku: "cns-002", unit: "Service", price: "750.50", vat: "0")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Consulting day", body.GetProperty("name").GetString());
        Assert.Equal("CNS-002", body.GetProperty("sku").GetString());
        Assert.Equal("Service", body.GetProperty("unit").GetString());
        Assert.Equal(750.50m, body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(0m, body.GetProperty("vatPercent").GetDecimal());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal("CNS-002", factory.ProductStore.All.Single().Sku);
    }

    [Theory]
    [InlineData("CNS-001")]
    [InlineData("cns-001")]
    [InlineData("  CNS-001 ")]
    public async Task Saving_with_its_own_sku_is_a_200(string ownSku)
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PutAsync($"/api/products/{product.Id}", Json(Body(name: "Renamed", sku: ownSku)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", (await JsonOf(response)).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("WEB-9")]
    [InlineData("web-9")]
    [InlineData("  Web-9 ")]
    public async Task Another_products_sku_is_a_409_and_the_product_is_unchanged(string otherSku)
    {
        await using var factory = new ApiFactory();
        Seed(factory, "WEB-9", "Website build");
        var mine = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PutAsync($"/api/products/{mine.Id}", Json(Body(name: "Changed", sku: otherSku)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("SKU already in use", body.GetProperty("title").GetString());
        Assert.Equal("Another product already uses this SKU.", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("Website", body.GetRawText());
        Assert.Equal("CNS-001", factory.ProductStore.All.Single(product => product.Id == mine.Id).Sku);
        Assert.Equal("Consulting hour", factory.ProductStore.All.Single(product => product.Id == mine.Id).Name);
    }

    [Theory]
    [InlineData("""{"name":"","sku":"CNS-001","unit":"Hour","unitPrice":100,"vatPercent":15}""", "name")]
    [InlineData("""{"name":"X","sku":"AB CD","unit":"Hour","unitPrice":100,"vatPercent":15}""", "sku")]
    [InlineData("""{"name":"X","sku":"CNS-001","unit":"Box","unitPrice":100,"vatPercent":15}""", "unit")]
    [InlineData("""{"name":"X","sku":"CNS-001","unit":"Hour","unitPrice":10.005,"vatPercent":15}""", "unitPrice")]
    [InlineData("""{"name":"X","sku":"CNS-001","unit":"Hour","unitPrice":-1,"vatPercent":15}""", "unitPrice")]
    [InlineData("""{"name":"X","sku":"CNS-001","unit":"Hour","unitPrice":100,"vatPercent":101}""", "vatPercent")]
    [InlineData("""{"name":"X","sku":"CNS-001","unit":"Hour","unitPrice":100}""", "vatPercent")]
    public async Task Each_validation_rule_is_a_400_naming_the_field_and_a_missing_vat_is_refused_on_edit(string json, string field)
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PutAsync($"/api/products/{product.Id}", Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal("Consulting hour", factory.ProductStore.All.Single().Name);
        Assert.Equal(15m, factory.ProductStore.All.Single().VatPercent);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2b9c1e-1111-4222-8333-444455556666")]
    [InlineData("not-a-guid")]
    public async Task An_unknown_or_malformed_id_is_a_404_on_edit_and_on_set_active(string id)
    {
        await using var factory = new ApiFactory();
        using var admin = factory.CreateAuthorizedClient("Admin");

        var edit = await admin.PutAsync($"/api/products/{id}", Json(Body()));
        var active = await admin.PutAsync($"/api/products/{id}/active", Json("""{"isActive":false}"""));

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal("Product not found", (await JsonOf(edit)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, active.StatusCode);
    }

    [Fact]
    public async Task Deactivating_hides_a_product_from_the_default_list_and_from_agents_and_reactivating_brings_it_back()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");
        using var agent = factory.CreateAuthorizedClient("Agent");

        var off = await admin.PutAsync($"/api/products/{product.Id}/active", Json("""{"isActive":false}"""));

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await JsonOf(off)).GetProperty("isActive").GetBoolean());
        Assert.Empty(await SkusOf(admin, string.Empty));
        Assert.Empty(await SkusOf(agent, "?status=all"));
        Assert.Equal(["CNS-001"], await SkusOf(admin, "?status=inactive"));
        Assert.Equal(["CNS-001"], await SkusOf(admin, "?status=all"));
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/products/{product.Id}")).StatusCode);

        var on = await admin.PutAsync($"/api/products/{product.Id}/active", Json("""{"isActive":true}"""));

        Assert.True((await JsonOf(on)).GetProperty("isActive").GetBoolean());
        Assert.Equal(["CNS-001"], await SkusOf(admin, string.Empty));
        Assert.Equal(["CNS-001"], await SkusOf(agent, string.Empty));
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deactivating_keeps_every_field()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var body = await JsonOf(await admin.PutAsync($"/api/products/{product.Id}/active", Json("""{"isActive":false}""")));

        Assert.Equal("Consulting hour", body.GetProperty("name").GetString());
        Assert.Equal("CNS-001", body.GetProperty("sku").GetString());
        Assert.Equal(100m, body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15m, body.GetProperty("vatPercent").GetDecimal());
    }

    [Fact]
    public async Task Setting_the_state_a_product_already_has_is_a_200_that_changes_nothing()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var first = await JsonOf(await admin.PutAsync($"/api/products/{product.Id}/active", Json("""{"isActive":true}""")));

        Assert.True(first.GetProperty("isActive").GetBoolean());
        Assert.Equal(first.GetProperty("createdAtUtc").GetDateTime(), first.GetProperty("updatedAtUtc").GetDateTime());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"isActive":null}""")]
    [InlineData("""{"isActive":"yes"}""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public async Task A_missing_or_non_boolean_is_active_is_a_400(string body)
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PutAsync($"/api/products/{product.Id}/active", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(factory.ProductStore.All.Single().IsActive);
    }

    [Fact]
    public async Task An_admin_can_still_edit_an_inactive_product()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "OLD-1", "Retired", active: false);
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PutAsync($"/api/products/{product.Id}", Json(Body(name: "Corrected", sku: "OLD-1")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Corrected", body.GetProperty("name").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_deactivated_products_sku_still_refuses_a_new_product()
    {
        await using var factory = new ApiFactory();
        Seed(factory, "OLD-1", "Retired", active: false);
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.PostAsync("/api/products", Json(Body(name: "Newcomer", sku: "old-1")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(factory.ProductStore.All);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    [InlineData("malformed")]
    public async Task An_agent_editing_a_product_gets_403_whatever_the_body_and_nothing_changes(string kind)
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var agent = factory.CreateAuthorizedClient("Agent");
        var body = kind switch
        {
            "valid" => Body(name: "Hacked", price: "1"),
            "invalid" => """{"name":"","sku":"AB CD","unit":"Box","unitPrice":-1}""",
            _ => "not json",
        };

        var response = await agent.PutAsync($"/api/products/{product.Id}", Json(body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Forbidden", (await JsonOf(response)).GetProperty("title").GetString());
        var stored = factory.ProductStore.All.Single();
        Assert.Equal("Consulting hour", stored.Name);
        Assert.Equal(100m, stored.UnitPrice);
    }

    [Theory]
    [InlineData("""{"isActive":false}""")]
    [InlineData("""{}""")]
    [InlineData("not json")]
    public async Task An_agent_changing_the_active_state_gets_403_and_nothing_changes(string body)
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var agent = factory.CreateAuthorizedClient("Agent");

        var response = await agent.PutAsync($"/api/products/{product.Id}/active", Json(body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(factory.ProductStore.All.Single().IsActive);
    }

    [Fact]
    public async Task An_agent_gets_403_even_for_an_unknown_id_so_nothing_about_existence_leaks()
    {
        await using var factory = new ApiFactory();
        using var agent = factory.CreateAuthorizedClient("Agent");

        var edit = await agent.PutAsync($"/api/products/{Guid.NewGuid()}", Json(Body()));
        var active = await agent.PutAsync($"/api/products/{Guid.NewGuid()}/active", Json("""{"isActive":false}"""));

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, active.StatusCode);
    }

    [Fact]
    public async Task There_is_no_delete_operation()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.DeleteAsync($"/api/products/{product.Id}");

        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert.Single(factory.ProductStore.All);
    }

    [Fact]
    public async Task Every_change_without_a_token_is_a_401()
    {
        await using var factory = new ApiFactory();
        var product = Seed(factory, "CNS-001", "Consulting hour");
        using var client = factory.CreateClient();

        var edit = await client.PutAsync($"/api/products/{product.Id}", Json(Body(name: "Hacked")));
        var active = await client.PutAsync($"/api/products/{product.Id}/active", Json("""{"isActive":false}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, active.StatusCode);
        Assert.Equal("Consulting hour", factory.ProductStore.All.Single().Name);
        Assert.True(factory.ProductStore.All.Single().IsActive);
    }
}
