using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class ProductServiceCreateTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeProductStore _store = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly ProductService _sut;

    public ProductServiceCreateTests()
    {
        _sut = new ProductService(_store, _time);
    }

    private static ProductInput Input(string sku = "CNS-001", string name = "Consulting hour", decimal? vat = 15m) =>
        new(name, sku, "Hour", 100m, vat);

    private Product SeedInactive(string sku, string name = "Retired")
    {
        var product = Product.Create(name, sku, ProductUnit.Item, 5m, 15m, Now.UtcDateTime);
        product.SetActive(false, Now.UtcDateTime);
        _store.Seed(product);
        return product;
    }

    [Fact]
    public async Task Creates_an_active_product_with_equal_timestamps_a_trimmed_name_and_an_upper_case_sku()
    {
        var result = await _sut.CreateAsync(Input(name: "  Consulting hour  ", sku: "  cns-001 "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var product = result.Value;
        Assert.Equal("Consulting hour", product.Name);
        Assert.Equal("CNS-001", product.Sku);
        Assert.Equal("Hour", product.Unit);
        Assert.Equal(100m, product.UnitPrice);
        Assert.Equal(15m, product.VatPercent);
        Assert.True(product.IsActive);
        Assert.Equal(Now.UtcDateTime, product.CreatedAtUtc);
        Assert.Equal(product.CreatedAtUtc, product.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Single(_store.All);
    }

    [Fact]
    public async Task A_missing_vat_is_saved_as_15()
    {
        var result = await _sut.CreateAsync(Input(vat: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(15m, result.Value.VatPercent);
    }

    [Fact]
    public async Task A_vat_of_zero_is_saved_as_zero()
    {
        var result = await _sut.CreateAsync(Input(vat: 0m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.VatPercent);
    }

    [Theory]
    [InlineData("CNS-001")]
    [InlineData("cns-001")]
    [InlineData("  Cns-001  ")]
    public async Task A_duplicate_sku_is_a_conflict_whatever_the_case_or_spacing_and_creates_nothing(string duplicate)
    {
        await _sut.CreateAsync(Input(), CancellationToken.None);

        var second = await _sut.CreateAsync(Input(sku: duplicate, name: "Someone Else"), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
        Assert.Equal("SKU already in use", second.Error.Title);
        Assert.Equal("Another product already uses this SKU.", second.Error.Detail);
        Assert.Single(_store.All);
    }

    [Fact]
    public async Task The_sku_of_an_inactive_product_is_still_taken()
    {
        SeedInactive("OLD-1", "Old");

        var result = await _sut.CreateAsync(Input(sku: "old-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Single(_store.All);
    }

    [Fact]
    public async Task Losing_the_race_at_the_database_gives_the_same_conflict()
    {
        _store.TryAddLosesRace = true;

        var result = await _sut.CreateAsync(Input(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal("Another product already uses this SKU.", result.Error.Detail);
        Assert.Empty(_store.All);
    }

    [Fact]
    public async Task An_invalid_input_stores_nothing_and_never_reaches_the_store()
    {
        var result = await _sut.CreateAsync(new ProductInput(null, "AB CD", "Box", 10.005m, 101m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(0, _store.TryAddCalls);
        Assert.Empty(_store.All);
    }

    [Fact]
    public async Task Get_returns_any_product_when_inactive_ones_are_allowed()
    {
        var created = SeedInactive("CNS-001");

        var result = await _sut.GetAsync(created.Id, includeInactive: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("CNS-001", result.Value.Sku);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task Get_of_an_active_product_works_when_inactive_ones_are_not_allowed()
    {
        var created = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;

        var result = await _sut.GetAsync(created.Id, includeInactive: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_inactive_product_looks_exactly_like_an_unknown_one_when_inactive_ones_are_not_allowed()
    {
        var created = SeedInactive("CNS-001");

        var hidden = await _sut.GetAsync(created.Id, includeInactive: false, CancellationToken.None);
        var unknown = await _sut.GetAsync(Guid.NewGuid(), includeInactive: false, CancellationToken.None);

        Assert.True(hidden.IsFailure);
        Assert.Equal(ErrorKind.NotFound, hidden.Error!.Kind);
        Assert.Equal(unknown.Error, hidden.Error);
        Assert.Equal("Product not found", hidden.Error.Title);
        Assert.Equal("No product exists with this id.", hidden.Error.Detail);
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_not_found()
    {
        var result = await _sut.GetAsync(Guid.NewGuid(), includeInactive: true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task List_with_default_paging_returns_only_active_products_when_inactive_ones_are_not_allowed()
    {
        await _sut.CreateAsync(Input(sku: "A-1", name: "Alpha"), CancellationToken.None);
        SeedInactive("B-1", "Beta");

        var result = await _sut.SearchAsync(new ProductQuery(null, null, null, null, null), includeInactive: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["A-1"], result.Value.Items.Select(item => item.Sku));
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Equal(1, result.Value.TotalCount);
    }
}
