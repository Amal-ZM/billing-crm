using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

public class CustomerSearchEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Customer Make(string name, string email, CustomerType type = CustomerType.Company, string phone = "0500000000", string? vat = null, bool active = true)
    {
        var customer = Customer.Create(name, type, email, phone, vat, "Somewhere", Now);
        if (!active)
        {
            customer.SetActive(false, Now);
        }

        return customer;
    }

    private static ApiFactory Seeded()
    {
        var factory = new ApiFactory();
        factory.CustomerStore.Seed(Make("Acme Trading", "accounts@acme.example", phone: "+966 11 555 0100", vat: "300000000000003"));
        factory.CustomerStore.Seed(Make("Baraka Foods", "hello@baraka.example", CustomerType.Company, "0555123456"));
        factory.CustomerStore.Seed(Make("Layla Hassan", "layla@mail.example", CustomerType.Individual, "0566000111", null));
        factory.CustomerStore.Seed(Make("Dormant Ltd", "old@dormant.example", CustomerType.Company, "0577000222", "VAT-OLD", active: false));
        factory.CustomerStore.Seed(Make("Old Person", "old.person@mail.example", CustomerType.Individual, "0588000333", null, active: false));
        return factory;
    }

    private static async Task<JsonElement> ListAsync(ApiFactory factory, string query = "")
    {
        using var client = factory.CreateAuthorizedClient();
        var response = await client.GetAsync("/api/customers" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task With_no_filters_both_active_and_inactive_are_listed_by_name_with_the_page_shape()
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory);

        Assert.Equal(new[] { "Acme Trading", "Baraka Foods", "Dormant Ltd", "Layla Hassan", "Old Person" }, Names(page));
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Inactive_customers_are_marked_in_the_list_items()
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory);

        var dormant = page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "Dormant Ltd");
        Assert.False(dormant.GetProperty("isActive").GetBoolean());
    }

    [Theory]
    [InlineData("acme", "Acme Trading")] // name, ignoring case
    [InlineData("HELLO@BARAKA", "Baraka Foods")] // email
    [InlineData("0566000", "Layla Hassan")] // phone
    [InlineData("VAT-OLD", "Dormant Ltd")] // VAT number
    [InlineData("300000000000003", "Acme Trading")]
    public async Task q_matches_a_fragment_of_name_email_phone_or_vat_number_ignoring_case(string q, string expected)
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, $"?q={Uri.EscapeDataString(q)}");

        Assert.Equal(new[] { expected }, Names(page));
    }

    [Fact]
    public async Task Whitespace_only_q_is_no_search()
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, "?q=%20%20");

        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("Company", 3)]
    [InlineData("Individual", 2)]
    [InlineData("company", 3)]
    public async Task The_type_filter_limits_the_list(string type, int expected)
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, $"?type={type}");

        Assert.Equal(expected, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("true", new[] { "Acme Trading", "Baraka Foods", "Layla Hassan" })]
    [InlineData("false", new[] { "Dormant Ltd", "Old Person" })]
    public async Task The_active_filter_limits_the_list(string active, string[] expected)
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, $"?active={active}");

        Assert.Equal(expected, Names(page));
    }

    [Fact]
    public async Task Search_and_both_filters_combine()
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, "?q=old&type=Individual&active=false");

        Assert.Equal(new[] { "Old Person" }, Names(page));
    }

    [Fact]
    public async Task No_match_is_a_200_with_empty_items_and_zero_totals()
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, "?q=nothing-matches-this");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Paging_reports_totals_and_a_page_beyond_the_end_is_empty_with_the_real_total()
    {
        await using var factory = Seeded();

        var second = await ListAsync(factory, "?page=2&pageSize=2");
        var beyond = await ListAsync(factory, "?page=9&pageSize=2");

        Assert.Equal(new[] { "Dormant Ltd", "Layla Hassan" }, Names(second));
        Assert.Equal(3, second.GetProperty("totalPages").GetInt32());
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        Assert.Equal(5, beyond.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?type=Partnership", "type")]
    [InlineData("?type=1", "type")]
    [InlineData("?active=yes", "active")]
    [InlineData("?page=0", "page")]
    [InlineData("?page=abc", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task Invalid_query_values_are_a_400_naming_the_field(string query, string field)
    {
        await using var factory = Seeded();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.GetAsync("/api/customers" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Page_size_100_is_the_largest_accepted()
    {
        await using var factory = Seeded();

        var page = await ListAsync(factory, "?pageSize=100");

        Assert.Equal(100, page.GetProperty("pageSize").GetInt32());
    }
}
