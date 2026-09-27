using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

public class InvoiceListEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Due() => DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record World(ApiFactory Factory, Customer Abc, Product Consulting);

    private static World NewWorld(string customerName = "ABC")
    {
        var factory = new ApiFactory();
        var abc = Customer.Create(customerName, CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(abc);
        var consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(consulting);
        return new World(factory, abc, consulting);
    }

    private static async Task<string> CreateAsync(HttpClient client, World world, Customer? customer = null)
    {
        var body = $"{{\"customerId\":\"{(customer ?? world.Abc).Id}\",\"dueDate\":\"{Due()}\",\"lines\":[{{\"productId\":\"{world.Consulting.Id}\",\"quantity\":1}}]}}";
        var response = await client.PostAsync("/api/invoices", Json(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("number").GetString()!;
    }

    private static async Task<List<string>> NumbersOf(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/invoices{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("number").GetString()!).ToList();
    }

    [Fact]
    public async Task Invoices_are_listed_newest_first()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var first = await CreateAsync(client, world);
        var second = await CreateAsync(client, world);
        var third = await CreateAsync(client, world);

        Assert.Equal([third, second, first], await NumbersOf(client, string.Empty));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Agent")]
    public async Task The_response_has_the_paged_shape_and_each_item_has_the_number_status_customer_due_date_and_created_time_but_no_lines_or_totals(string role)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient(role);
        await CreateAsync(client, world);

        var body = await JsonOf(await client.GetAsync("/api/invoices"));

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("totalPages").GetInt32());
        var item = body.GetProperty("items")[0];
        Assert.Equal("INV-000001", item.GetProperty("number").GetString());
        Assert.Equal("Draft", item.GetProperty("status").GetString());
        Assert.Equal(world.Abc.Id, item.GetProperty("customerId").GetGuid());
        Assert.Equal("ABC", item.GetProperty("customerName").GetString());
        Assert.Equal(Due(), item.GetProperty("dueDate").GetString());
        Assert.True(item.TryGetProperty("createdAtUtc", out _));
        var names = item.EnumerateObject().Select(property => property.Name).ToList();
        Assert.DoesNotContain(names, name => name.Contains("line", StringComparison.OrdinalIgnoreCase)
            || name.Contains("total", StringComparison.OrdinalIgnoreCase)
            || name.Contains("amount", StringComparison.OrdinalIgnoreCase)
            || name.Contains("balance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_customer_name_is_the_customers_current_name()
    {
        var world = NewWorld("ABC");
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");
        await CreateAsync(client, world);
        await client.PutAsync($"/api/customers/{world.Abc.Id}", Json("""{"name":"شركة الأمل","type":"Company","email":"accounts@abc.example","phone":"0500000000","address":"Riyadh"}"""));

        var body = await JsonOf(await client.GetAsync("/api/invoices"));

        Assert.Equal("شركة الأمل", body.GetProperty("items")[0].GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task No_invoices_is_a_200_with_empty_items_and_zero_totals()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");

        var body = await JsonOf(await client.GetAsync("/api/invoices"));

        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Paging_splits_the_results_and_keeps_a_stable_order()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        var numbers = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            numbers.Add(await CreateAsync(client, world));
        }

        numbers.Reverse();
        Assert.Equal(numbers.Take(2), await NumbersOf(client, "?pageSize=2&page=1"));
        Assert.Equal(numbers.Skip(2).Take(2), await NumbersOf(client, "?pageSize=2&page=2"));
        Assert.Equal(numbers.Skip(4), await NumbersOf(client, "?pageSize=2&page=3"));
        var body = await JsonOf(await client.GetAsync("/api/invoices?pageSize=2&page=3"));
        Assert.Equal(5, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, body.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("?page=3&pageSize=2")]
    [InlineData("?page=2147483647")]
    [InlineData("?page=2147483647&pageSize=100")]
    public async Task A_page_beyond_the_last_is_a_200_with_empty_items_and_the_real_total(string query)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");
        await CreateAsync(client, world);
        await CreateAsync(client, world);

        var response = await client.GetAsync($"/api/invoices{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?page=abc", "page")]
    [InlineData("?page=99999999999", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?pageSize=abc", "pageSize")]
    public async Task Invalid_paging_values_are_a_400_naming_the_field(string query, string field)
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync($"/api/invoices{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await JsonOf(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task A_page_size_of_100_is_accepted()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateAuthorizedClient("Agent");

        var response = await client.GetAsync("/api/invoices?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, (await JsonOf(response)).GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task The_list_needs_a_token()
    {
        var world = NewWorld();
        await using var factoryScope = world.Factory;
        using var client = world.Factory.CreateClient();

        var response = await client.GetAsync("/api/invoices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
