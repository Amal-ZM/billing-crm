using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class ProductServiceSearchTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeProductStore _store = new();
    private readonly ProductService _sut;

    public ProductServiceSearchTests()
    {
        _sut = new ProductService(_store, new FixedTimeProvider(Now));
    }

    private static ProductQuery Query(string? q = null, string? unit = null, string? status = null, string? page = null, string? pageSize = null) =>
        new(q, unit, status, page, pageSize);

    private async Task<IReadOnlyDictionary<string, string[]>> ErrorsOf(ProductQuery query, bool includeInactive = true)
    {
        var result = await _sut.SearchAsync(query, includeInactive, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public async Task A_bad_page_is_a_field_error(string page)
    {
        Assert.True((await ErrorsOf(Query(page: page))).ContainsKey("page"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("abc")]
    public async Task A_bad_page_size_is_a_field_error(string pageSize)
    {
        Assert.True((await ErrorsOf(Query(pageSize: pageSize))).ContainsKey("pageSize"));
    }

    [Fact]
    public async Task An_unknown_unit_is_a_field_error()
    {
        Assert.True((await ErrorsOf(Query(unit: "Box"))).ContainsKey("unit"));
        Assert.True((await ErrorsOf(Query(unit: "1"))).ContainsKey("unit"));
    }

    [Fact]
    public async Task An_unknown_status_is_a_field_error_for_a_caller_who_may_see_inactive_products()
    {
        Assert.True((await ErrorsOf(Query(status: "everything"), includeInactive: true)).ContainsKey("status"));
    }

    [Fact]
    public async Task Search_text_over_100_characters_is_a_field_error()
    {
        Assert.True((await ErrorsOf(Query(q: new string('x', 101)))).ContainsKey("q"));
    }

    [Fact]
    public async Task Several_bad_values_are_all_named()
    {
        var errors = await ErrorsOf(Query(unit: "Box", status: "zzz", page: "0", pageSize: "500"));

        Assert.Equal(["page", "pageSize", "status", "unit"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Hour", ProductUnit.Hour)]
    [InlineData("hour", ProductUnit.Hour)]
    [InlineData("ITEM", ProductUnit.Item)]
    [InlineData("Service", ProductUnit.Service)]
    public async Task A_unit_is_accepted_in_any_letter_case(string unit, ProductUnit expected)
    {
        var result = await _sut.SearchAsync(Query(unit: unit), includeInactive: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, _store.LastSearch!.Unit);
    }

    [Fact]
    public async Task Valid_edges_pass_and_reach_the_store_as_given()
    {
        var result = await _sut.SearchAsync(Query(q: "cns", unit: "Item", status: "ALL", page: "3", pageSize: "100"), includeInactive: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ProductSearch("cns", ProductUnit.Item, ProductStatusFilter.All, 3, 100), _store.LastSearch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_text_means_no_search(string? q)
    {
        await _sut.SearchAsync(Query(q: q), includeInactive: true, CancellationToken.None);

        Assert.Null(_store.LastSearch!.Text);
    }

    [Fact]
    public async Task Search_text_is_trimmed()
    {
        await _sut.SearchAsync(Query(q: "  cns  "), includeInactive: true, CancellationToken.None);

        Assert.Equal("cns", _store.LastSearch!.Text);
    }

    [Theory]
    [InlineData(null, ProductStatusFilter.Active)]
    [InlineData("", ProductStatusFilter.Active)]
    [InlineData("active", ProductStatusFilter.Active)]
    [InlineData("Inactive", ProductStatusFilter.Inactive)]
    [InlineData("all", ProductStatusFilter.All)]
    public async Task For_a_caller_who_may_see_inactive_products_the_status_is_used_and_defaults_to_active(string? status, ProductStatusFilter expected)
    {
        var result = await _sut.SearchAsync(Query(status: status), includeInactive: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, _store.LastSearch!.Status);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("inactive")]
    [InlineData("all")]
    [InlineData("everything")]
    public async Task For_a_caller_who_may_not_see_inactive_products_the_list_is_always_active_only_and_status_is_ignored(string status)
    {
        var result = await _sut.SearchAsync(Query(status: status), includeInactive: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatusFilter.Active, _store.LastSearch!.Status);
    }

    [Fact]
    public async Task Results_map_to_list_items_and_carry_the_totals()
    {
        _store.Seed(Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now.UtcDateTime));

        var result = await _sut.SearchAsync(Query(), includeInactive: false, CancellationToken.None);

        var item = Assert.Single(result.Value.Items);
        Assert.Equal("CNS-001", item.Sku);
        Assert.Equal("Hour", item.Unit);
        Assert.Equal(100m, item.UnitPrice);
        Assert.Equal(15m, item.VatPercent);
        Assert.True(item.IsActive);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(1, result.Value.TotalPages);
    }
}
