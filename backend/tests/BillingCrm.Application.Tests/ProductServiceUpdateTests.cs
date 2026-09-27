using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class ProductServiceUpdateTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeProductStore _store = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly ProductService _sut;

    public ProductServiceUpdateTests()
    {
        _sut = new ProductService(_store, _time);
    }

    private static ProductInput Input(string name = "Consulting hour", string sku = "CNS-001", decimal price = 100m, decimal? vat = 15m, string unit = "Hour") =>
        new(name, sku, unit, price, vat);

    private async Task<ProductDto> CreateAsync(ProductInput input) =>
        (await _sut.CreateAsync(input, CancellationToken.None)).Value;

    [Fact]
    public async Task A_valid_update_changes_the_fields_and_moves_the_changed_time_but_not_the_created_time()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(2));

        var result = await _sut.UpdateAsync(
            created.Id,
            new ProductInput("  Consulting day  ", "cns-001", "service", 750.50m, 0m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Consulting day", result.Value.Name);
        Assert.Equal("CNS-001", result.Value.Sku);
        Assert.Equal("Service", result.Value.Unit);
        Assert.Equal(750.50m, result.Value.UnitPrice);
        Assert.Equal(0m, result.Value.VatPercent);
        Assert.Equal(Start.UtcDateTime, result.Value.CreatedAtUtc);
        Assert.Equal(Start.AddHours(2).UtcDateTime, result.Value.UpdatedAtUtc);
        Assert.Equal(1, _store.TryUpdateCalls);
    }

    [Theory]
    [InlineData("CNS-001")]
    [InlineData("cns-001")]
    [InlineData("  Cns-001 ")]
    public async Task Saving_a_product_with_its_own_sku_is_not_a_duplicate(string ownSku)
    {
        var created = await CreateAsync(Input());

        var result = await _sut.UpdateAsync(created.Id, Input(name: "Renamed", sku: ownSku), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", result.Value.Name);
        Assert.Equal("CNS-001", result.Value.Sku);
    }

    [Theory]
    [InlineData("WEB-9")]
    [InlineData("web-9")]
    [InlineData("  Web-9 ")]
    public async Task Another_products_sku_is_a_conflict_and_the_product_is_unchanged(string otherSku)
    {
        await CreateAsync(Input(name: "Website", sku: "WEB-9"));
        var mine = await CreateAsync(Input());

        var result = await _sut.UpdateAsync(mine.Id, Input(name: "Changed", sku: otherSku), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal("Another product already uses this SKU.", result.Error.Detail);
        var stored = _store.All.Single(product => product.Id == mine.Id);
        Assert.Equal("CNS-001", stored.Sku);
        Assert.Equal("Consulting hour", stored.Name);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task The_sku_of_an_inactive_product_is_still_taken_when_editing()
    {
        var retired = Product.Create("Old", "OLD-1", ProductUnit.Item, 5m, 15m, Start.UtcDateTime);
        retired.SetActive(false, Start.UtcDateTime);
        _store.Seed(retired);
        var mine = await CreateAsync(Input());

        var result = await _sut.UpdateAsync(mine.Id, Input(sku: "old-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
    }

    [Fact]
    public async Task Losing_the_race_at_the_database_gives_the_same_conflict()
    {
        var created = await CreateAsync(Input());
        _store.TryUpdateLosesRace = true;

        var result = await _sut.UpdateAsync(created.Id, Input(name: "Changed"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_leaves_the_changed_time_alone_and_does_not_touch_the_store()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(5));

        var result = await _sut.UpdateAsync(created.Id, Input(sku: " cns-001 "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Start.UtcDateTime, result.Value.UpdatedAtUtc);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task Validation_is_as_on_create_but_a_missing_vat_is_an_error_not_a_default()
    {
        var created = await CreateAsync(Input());

        var missingVat = await _sut.UpdateAsync(created.Id, Input(vat: null), CancellationToken.None);
        var bad = await _sut.UpdateAsync(created.Id, new ProductInput("", "AB CD", "Box", 10.005m, 101m), CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, missingVat.Error!.Kind);
        Assert.True(missingVat.Error.Errors!.ContainsKey("vatPercent"));
        Assert.Equal(["name", "sku", "unit", "unitPrice", "vatPercent"], bad.Error!.Errors!.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(15m, _store.All.Single().VatPercent);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task An_unknown_id_is_not_found_on_update_and_on_set_active()
    {
        var update = await _sut.UpdateAsync(Guid.NewGuid(), Input(), CancellationToken.None);
        var setActive = await _sut.SetActiveAsync(Guid.NewGuid(), new ProductActiveRequest(false), CancellationToken.None);

        Assert.Equal(ErrorKind.NotFound, update.Error!.Kind);
        Assert.Equal("Product not found", update.Error.Title);
        Assert.Equal(ErrorKind.NotFound, setActive.Error!.Kind);
    }

    [Fact]
    public async Task An_inactive_product_can_be_edited()
    {
        var created = await CreateAsync(Input());
        await _sut.SetActiveAsync(created.Id, new ProductActiveRequest(false), CancellationToken.None);

        var result = await _sut.UpdateAsync(created.Id, Input(name: "Fixed while retired", price: 120m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fixed while retired", result.Value.Name);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task Deactivating_and_reactivating_changes_only_the_active_state_and_the_changed_time()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(1));

        var off = await _sut.SetActiveAsync(created.Id, new ProductActiveRequest(false), CancellationToken.None);

        Assert.True(off.IsSuccess);
        Assert.False(off.Value.IsActive);
        Assert.Equal(created.Name, off.Value.Name);
        Assert.Equal(created.Sku, off.Value.Sku);
        Assert.Equal(created.UnitPrice, off.Value.UnitPrice);
        Assert.Equal(created.VatPercent, off.Value.VatPercent);
        Assert.Equal(Start.AddHours(1).UtcDateTime, off.Value.UpdatedAtUtc);

        _time.Advance(TimeSpan.FromHours(1));
        var on = await _sut.SetActiveAsync(created.Id, new ProductActiveRequest(true), CancellationToken.None);

        Assert.True(on.Value.IsActive);
        Assert.Equal(Start.AddHours(2).UtcDateTime, on.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task Setting_the_state_a_product_already_has_changes_nothing()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(3));

        var result = await _sut.SetActiveAsync(created.Id, new ProductActiveRequest(true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Start.UtcDateTime, result.Value.UpdatedAtUtc);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task A_missing_is_active_is_a_validation_error()
    {
        var created = await CreateAsync(Input());

        var result = await _sut.SetActiveAsync(created.Id, new ProductActiveRequest(null), CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.True(result.Error.Errors!.ContainsKey("isActive"));
        Assert.True(_store.All.Single().IsActive);
    }

    [Fact]
    public async Task A_deactivated_products_sku_still_refuses_a_new_product()
    {
        var created = await CreateAsync(Input());
        await _sut.SetActiveAsync(created.Id, new ProductActiveRequest(false), CancellationToken.None);

        var result = await _sut.CreateAsync(Input(name: "Newcomer"), CancellationToken.None);

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Single(_store.All);
    }
}
