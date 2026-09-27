using BillingCrm.Application.Products;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Products;

namespace BillingCrm.Infrastructure.Tests;

public class EfProductStoreUpdateTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(2);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Product> AddAsync(string sku, string name = "Consulting hour", decimal price = 100.10m)
    {
        var product = Product.Create(name, sku, ProductUnit.Hour, price, 15m, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private async Task<Product> LoadAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return (await new EfProductStore(context).FindByIdAsync(id, default))!;
    }

    [Fact]
    public async Task TryUpdate_persists_changes_including_arabic_text_and_a_changed_price_exactly()
    {
        var added = await AddAsync(Unique("update"));

        await using (var context = database.CreateContext())
        {
            var store = new EfProductStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            Assert.True(tracked.Update("ساعة استشارة", tracked.Sku, ProductUnit.Service, 250.55m, 0m, Later));
            Assert.True(await store.TryUpdateAsync(tracked, default));
        }

        var loaded = await LoadAsync(added.Id);
        Assert.Equal("ساعة استشارة", loaded.Name);
        Assert.Equal(ProductUnit.Service, loaded.Unit);
        Assert.Equal(250.55m, loaded.UnitPrice);
        Assert.Equal(0m, loaded.VatPercent);
        Assert.Equal(Later, loaded.UpdatedAtUtc);
        Assert.Equal(Now, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Taking_another_products_sku_returns_false_and_the_stored_row_keeps_its_old_sku()
    {
        var other = await AddAsync(Unique("other"), name: "Other");
        var mine = await AddAsync(Unique("mine"));

        await using (var context = database.CreateContext())
        {
            var store = new EfProductStore(context);
            var tracked = (await store.FindByIdAsync(mine.Id, default))!;
            tracked.Update("Changed Name", other.Sku, tracked.Unit, tracked.UnitPrice, tracked.VatPercent, Later);

            Assert.False(await store.TryUpdateAsync(tracked, default));

            // The rejected edit is forgotten, so this context does not go on believing it happened.
            Assert.Equal(mine.Sku, tracked.Sku);
            Assert.Equal("Consulting hour", tracked.Name);
        }

        var stored = await LoadAsync(mine.Id);
        Assert.Equal(mine.Sku, stored.Sku);
        Assert.Equal("Consulting hour", stored.Name);
        Assert.Equal(Now, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task The_store_stays_usable_after_a_refused_update()
    {
        var other = await AddAsync(Unique("other2"), name: "Other");
        var mine = await AddAsync(Unique("mine2"));

        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        var tracked = (await store.FindByIdAsync(mine.Id, default))!;
        tracked.Update("Changed", other.Sku, tracked.Unit, tracked.UnitPrice, tracked.VatPercent, Later);
        Assert.False(await store.TryUpdateAsync(tracked, default));

        tracked.Update("Changed again", tracked.Sku, tracked.Unit, tracked.UnitPrice, tracked.VatPercent, Later);
        Assert.True(await store.TryUpdateAsync(tracked, default));
        Assert.Equal("Changed again", (await LoadAsync(mine.Id)).Name);
    }

    [Fact]
    public async Task Saving_a_product_with_its_own_sku_succeeds()
    {
        var added = await AddAsync(Unique("own"));

        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        var tracked = (await store.FindByIdAsync(added.Id, default))!;
        tracked.Update("Renamed", tracked.Sku, tracked.Unit, tracked.UnitPrice, tracked.VatPercent, Later);

        Assert.True(await store.TryUpdateAsync(tracked, default));
        Assert.Equal("Renamed", (await LoadAsync(added.Id)).Name);
        Assert.False(await store.SkuExistsAsync(added.Sku, added.Id, default));
    }

    [Fact]
    public async Task Deactivating_keeps_the_row_and_every_field_and_the_sku_stays_reserved()
    {
        var added = await AddAsync(Unique("dormant"), price: 999999999.99m);

        await using (var context = database.CreateContext())
        {
            var store = new EfProductStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            Assert.True(tracked.SetActive(false, Later));
            Assert.True(await store.TryUpdateAsync(tracked, default));
        }

        var loaded = await LoadAsync(added.Id);
        Assert.False(loaded.IsActive);
        Assert.Equal(added.Name, loaded.Name);
        Assert.Equal(added.Sku, loaded.Sku);
        Assert.Equal(ProductUnit.Hour, loaded.Unit);
        Assert.Equal(999999999.99m, loaded.UnitPrice);
        Assert.Equal(15m, loaded.VatPercent);

        var newcomer = Product.Create("Newcomer", added.Sku, ProductUnit.Item, 1m, 15m, Later);
        await using var second = database.CreateContext();
        Assert.False(await new EfProductStore(second).TryAddAsync(newcomer, default));
    }

    [Fact]
    public async Task An_inactive_product_is_found_by_the_inactive_and_all_filters_and_not_by_the_active_one()
    {
        var added = await AddAsync(Unique("findme"), name: $"Findme {Guid.NewGuid():N}");
        await using (var context = database.CreateContext())
        {
            var store = new EfProductStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            tracked.SetActive(false, Later);
            await store.TryUpdateAsync(tracked, default);
        }

        await using var read = database.CreateContext();
        var store2 = new EfProductStore(read);

        var active = await store2.SearchAsync(new ProductSearch(added.Name, null, ProductStatusFilter.Active, 1, 10), default);
        var inactive = await store2.SearchAsync(new ProductSearch(added.Name, null, ProductStatusFilter.Inactive, 1, 10), default);
        var all = await store2.SearchAsync(new ProductSearch(added.Name, null, ProductStatusFilter.All, 1, 10), default);

        Assert.Empty(active.Items);
        Assert.Single(inactive.Items);
        Assert.Single(all.Items);
        Assert.True(await store2.SkuExistsAsync(added.Sku, null, default));
    }

    [Fact]
    public async Task Reactivating_brings_the_product_back_into_the_active_list()
    {
        var added = await AddAsync(Unique("back"), name: $"Back {Guid.NewGuid():N}");
        await using (var context = database.CreateContext())
        {
            var store = new EfProductStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            tracked.SetActive(false, Later);
            await store.TryUpdateAsync(tracked, default);
        }

        await using (var context = database.CreateContext())
        {
            var store = new EfProductStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            tracked.SetActive(true, Later.AddHours(1));
            Assert.True(await store.TryUpdateAsync(tracked, default));
        }

        await using var read = database.CreateContext();
        var found = await new EfProductStore(read).SearchAsync(new ProductSearch(added.Name, null, ProductStatusFilter.Active, 1, 10), default);
        Assert.Single(found.Items);
        Assert.Equal(Later.AddHours(1), found.Items[0].UpdatedAtUtc);
    }
}
