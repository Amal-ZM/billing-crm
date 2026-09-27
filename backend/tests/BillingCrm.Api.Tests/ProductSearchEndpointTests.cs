using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

public class ProductSearchEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Product Make(string sku, string name, ProductUnit unit = ProductUnit.Item, bool active = true)
    {
        var product = Product.Create(name, sku, unit, 10m, 15m, Now);
        if (!active)
        {
            product.SetActive(false, Now);
        }

        return product;
    }

    private static ApiFactory Factory(params Product[] products)
    {
        var factory = new ApiFactory();
        foreach (var product in products)
        {
            factory.ProductStore.Seed(product);
        }

        return factory;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<List<string>> SkusOf(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/products{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sku").GetString()!).ToList();
    }

    [Fact]
    public async Task Search_matches_a_fragment_of_the_name_or_the_sku_ignoring_letter_case()
    {
        await using var factory = Factory(
            Make("CNS-001", "Consulting hour"),
            Make("WEB-9", "Website build"),
            Make("SUP-1", "Support plan"));
        using var client = factory.CreateAuthorizedClient("Agent");

        Assert.Equal(["CNS-001"], await SkusOf(client, "?q=consult"));
        Assert.Equal(["CNS-001"], await SkusOf(client, "?q=CONSULT"));
        Assert.Equal(["WEB-9"], await SkusOf(client, "?q=web-"));
        Assert.Equal(["WEB-9"], await SkusOf(client, "?q=web-9"));
        Assert.Equal(["SUP-1"], await SkusOf(client, "?q=%20sup%20"));
    }

    [Fact]
    public async Task Arabic_names_are_found_by_a_substring()
    {
        await using var factory = Factory(Make("AR-1", "ساعة استشارة"), Make("EN-1", "Consulting"));
        using var client = factory.CreateAuthorizedClient("Agent");

        Assert.Equal(["AR-1"], await SkusOf(client, "?q=" + Uri.EscapeDataString("استشارة")));
    }

    [Fact]
    public async Task A_blank_search_means_no_search()
    {
        await using var factory = Factory(Make("A-1", "Alpha"), Make("B-1", "Beta"));
        using var client = factory.CreateAuthorizedClient("Agent");

        Assert.Equal(["A-1", "B-1"], await SkusOf(client, "?q=%20%20"));
        Assert.Equal(["A-1", "B-1"], await SkusOf(client, "?q="));
    }

    [Fact]
    public async Task The_unit_filter_works_alone_and_combined_with_the_search()
    {
        await using var factory = Factory(
            Make("H-1", "Alpha hour", ProductUnit.Hour),
            Make("H-2", "Beta hour", ProductUnit.Hour),
            Make("I-1", "Alpha item", ProductUnit.Item),
            Make("S-1", "Alpha service", ProductUnit.Service));
        using var client = factory.CreateAuthorizedClient("Agent");

        Assert.Equal(["H-1", "H-2"], await SkusOf(client, "?unit=Hour"));
        Assert.Equal(["H-1", "H-2"], await SkusOf(client, "?unit=hour"));
        Assert.Equal(["I-1"], await SkusOf(client, "?unit=Item"));
        Assert.Equal(["S-1"], await SkusOf(client, "?unit=Service"));
        Assert.Equal(["H-1"], await SkusOf(client, "?unit=Hour&q=alpha"));
        Assert.Empty(await SkusOf(client, "?unit=Service&q=beta"));
    }

    [Fact]
    public async Task The_default_list_has_active_products_only()
    {
        await using var factory = Factory(Make("A-1", "Alpha"), Make("Z-1", "Zulu", active: false));
        using var admin = factory.CreateAuthorizedClient("Admin");

        Assert.Equal(["A-1"], await SkusOf(admin, string.Empty));
        Assert.Equal(["A-1"], await SkusOf(admin, "?status=active"));
    }

    [Fact]
    public async Task An_admin_can_ask_for_inactive_or_all_products()
    {
        await using var factory = Factory(Make("A-1", "Alpha"), Make("Z-1", "Zulu", active: false));
        using var admin = factory.CreateAuthorizedClient("Admin");

        Assert.Equal(["Z-1"], await SkusOf(admin, "?status=inactive"));
        Assert.Equal(["A-1", "Z-1"], await SkusOf(admin, "?status=all"));
        Assert.Equal(["A-1", "Z-1"], await SkusOf(admin, "?status=ALL"));
    }

    [Theory]
    [InlineData("?status=all")]
    [InlineData("?status=inactive")]
    [InlineData("?status=everything")]
    public async Task An_agent_always_gets_active_products_only_whatever_status_is_sent(string query)
    {
        await using var factory = Factory(Make("A-1", "Alpha"), Make("Z-1", "Zulu", active: false));
        using var agent = factory.CreateAuthorizedClient("Agent");

        var response = await agent.GetAsync($"/api/products{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal("A-1", body.GetProperty("items")[0].GetProperty("sku").GetString());
    }

    [Fact]
    public async Task An_admin_with_a_bad_status_gets_a_400_naming_it()
    {
        await using var factory = Factory(Make("A-1", "Alpha"));
        using var admin = factory.CreateAuthorizedClient("Admin");

        var response = await admin.GetAsync("/api/products?status=everything");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task The_response_has_the_paged_shape_with_prices_and_vat_as_numbers()
    {
        await using var factory = Factory(Make("A-1", "Alpha"));
        using var client = factory.CreateAuthorizedClient("Agent");

        var body = await JsonOf(await client.GetAsync("/api/products"));

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("totalPages").GetInt32());
        var item = body.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Number, item.GetProperty("unitPrice").ValueKind);
        Assert.Equal(JsonValueKind.Number, item.GetProperty("vatPercent").ValueKind);
        Assert.Equal(10m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Item", item.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task No_match_is_a_200_with_empty_items_and_zero_totals()
    {
        await using var factory = Factory(Make("A-1", "Alpha"));
        using var client = factory.CreateAuthorizedClient("Agent");

        var body = await JsonOf(await client.GetAsync("/api/products?q=zzz"));

        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Paging_splits_the_results_and_keeps_a_stable_order()
    {
        await using var factory = Factory(Enumerable.Range(1, 5).Select(n => Make($"P-{n}", $"Product {n}")).ToArray());
        using var client = factory.CreateAuthorizedClient("Agent");

        Assert.Equal(["P-1", "P-2"], await SkusOf(client, "?pageSize=2&page=1"));
        Assert.Equal(["P-3", "P-4"], await SkusOf(client, "?pageSize=2&page=2"));
        Assert.Equal(["P-5"], await SkusOf(client, "?pageSize=2&page=3"));

        var body = await JsonOf(await client.GetAsync("/api/products?pageSize=2&page=3"));
        Assert.Equal(5, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, body.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("?page=3")]
    [InlineData("?page=2147483647")]
    public async Task A_page_beyond_the_last_is_a_200_with_empty_items_and_the_real_total(string query)
    {
        await using var factory = Factory(Make("A-1", "Alpha"), Make("B-1", "Beta"));
        using var client = factory.CreateAuthorizedClient("Agent");

        var response = await client.GetAsync($"/api/products{query}&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?unit=Box", "unit")]
    [InlineData("?page=0", "page")]
    [InlineData("?page=abc", "page")]
    [InlineData("?page=99999999999", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task Invalid_query_values_are_a_400_naming_the_field(string query, string field)
    {
        await using var factory = Factory(Make("A-1", "Alpha"));
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync($"/api/products{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Search_text_over_100_characters_is_a_400_naming_q()
    {
        await using var factory = Factory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync($"/api/products?q={new string('x', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty("q", out _));
    }

    [Fact]
    public async Task An_inactive_product_opened_by_id_is_a_404_for_an_agent_and_a_200_for_an_admin()
    {
        var retired = Make("Z-1", "Zulu", active: false);
        await using var factory = Factory(retired);
        using var agent = factory.CreateAuthorizedClient("Agent");
        using var admin = factory.CreateAuthorizedClient("Admin");

        var forAgent = await agent.GetAsync($"/api/products/{retired.Id}");
        var forUnknown = await agent.GetAsync($"/api/products/{Guid.NewGuid()}");
        var forAdmin = await admin.GetAsync($"/api/products/{retired.Id}");

        Assert.Equal(HttpStatusCode.NotFound, forAgent.StatusCode);
        Assert.Equal(await forUnknown.Content.ReadAsStringAsync(), await forAgent.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, forAdmin.StatusCode);
        Assert.False((await JsonOf(forAdmin)).GetProperty("isActive").GetBoolean());
    }
}
