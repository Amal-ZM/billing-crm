using BillingCrm.Application.Products;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

public class EfProductStoreCreateTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Product NewProduct(string sku, string name = "Consulting hour", decimal price = 100m, decimal vat = 15m) =>
        Product.Create(name, sku, ProductUnit.Hour, price, vat, Now);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    [Fact]
    public async Task TryAdd_persists_every_field_including_an_arabic_name()
    {
        var product = Product.Create("ساعة استشارة", Unique("ar"), ProductUnit.Service, 250.75m, 5m, Now);

        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        }

        await using var read = database.CreateContext();
        var loaded = await new EfProductStore(read).FindByIdAsync(product.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("ساعة استشارة", loaded.Name);
        Assert.Equal(product.Sku, loaded.Sku);
        Assert.Equal(ProductUnit.Service, loaded.Unit);
        Assert.Equal(250.75m, loaded.UnitPrice);
        Assert.Equal(5m, loaded.VatPercent);
        Assert.True(loaded.IsActive);
        Assert.Equal(Now, loaded.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal(Now, loaded.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("100.10", "15.00")]
    [InlineData("999999999.99", "100.00")]
    [InlineData("0.01", "0.00")]
    [InlineData("0.10", "12.50")]
    public async Task Money_and_vat_round_trip_as_exactly_the_same_decimals(string price, string vat)
    {
        var expectedPrice = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        var expectedVat = decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture);
        var product = NewProduct(Unique("money"), price: expectedPrice, vat: expectedVat);

        await using (var context = database.CreateContext())
        {
            await new EfProductStore(context).TryAddAsync(product, default);
        }

        await using var read = database.CreateContext();
        var loaded = (await new EfProductStore(read).FindByIdAsync(product.Id, default))!;
        Assert.Equal(expectedPrice, loaded.UnitPrice);
        Assert.Equal(expectedVat, loaded.VatPercent);
    }

    [Fact]
    public async Task A_second_add_with_the_same_sku_returns_false_and_stores_nothing()
    {
        var sku = Unique("dup");

        await using var first = database.CreateContext();
        Assert.True(await new EfProductStore(first).TryAddAsync(NewProduct(sku), default));

        var duplicate = NewProduct(sku, name: "Someone Else");
        await using var second = database.CreateContext();
        Assert.False(await new EfProductStore(second).TryAddAsync(duplicate, default));

        await using var read = database.CreateContext();
        Assert.Equal(1, await read.Products.CountAsync(product => product.Sku == sku));
        Assert.Null(await new EfProductStore(read).FindByIdAsync(duplicate.Id, default));
    }

    [Fact]
    public async Task The_store_stays_usable_after_a_refused_duplicate()
    {
        var sku = Unique("reuse");
        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        Assert.True(await store.TryAddAsync(NewProduct(sku), default));
        Assert.False(await store.TryAddAsync(NewProduct(sku, name: "Dup"), default));

        Assert.True(await store.TryAddAsync(NewProduct(Unique("after")), default));
    }

    [Fact]
    public async Task The_unique_index_itself_rejects_a_duplicate_even_when_the_pre_check_is_bypassed_and_ignores_letter_case()
    {
        var sku = Unique("race");
        await using var context = database.CreateContext();
        const string insert =
            "INSERT INTO Products (Id, Name, Sku, Unit, UnitPrice, VatPercent, IsActive, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (@id, N'A', @sku, 'Item', 1, 15, 1, SYSUTCDATETIME(), SYSUTCDATETIME())";

        await context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("sku", sku));

        var exact = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("sku", sku)));
        var otherCase = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("sku", sku.ToLowerInvariant())));

        // An index (not a constraint) is what EF creates, so SQL Server reports 2601, naming the index.
        Assert.Equal(2601, exact.Number);
        Assert.Contains("IX_Products_Sku", exact.Message);
        Assert.Equal(2601, otherCase.Number);
    }

    [Theory]
    [InlineData("'Box'", "1", "15")]
    [InlineData("'Item'", "-0.01", "15")]
    [InlineData("'Item'", "1000000000.00", "15")] // spec 012: closes a 004 deferred item, the database now also enforces ProductValidator.MaxUnitPrice
    [InlineData("'Item'", "1", "100.01")]
    [InlineData("'Item'", "1", "-1")]
    public async Task The_check_constraints_reject_an_unknown_unit_a_negative_price_and_a_vat_out_of_range(string unit, string price, string vat)
    {
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (Id, Name, Sku, Unit, UnitPrice, VatPercent, IsActive, CreatedAtUtc, UpdatedAtUtc) " +
            $"VALUES (NEWID(), N'A', @sku, {unit}, {price}, {vat}, 1, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new SqlParameter("sku", Unique("check"))));

        Assert.Equal(547, exception.Number);
    }

    [Fact]
    public async Task SkuExists_finds_by_sku_and_can_exclude_one_product()
    {
        var product = NewProduct(Unique("exists"));
        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        await store.TryAddAsync(product, default);

        Assert.True(await store.SkuExistsAsync(product.Sku, null, default));
        Assert.False(await store.SkuExistsAsync(product.Sku, product.Id, default));
        Assert.False(await store.SkuExistsAsync(Unique("other"), null, default));
    }

    [Fact]
    public async Task Search_with_default_paging_orders_by_name_then_id_and_counts_everything()
    {
        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        var marker = Guid.NewGuid().ToString("N")[..8];
        foreach (var name in new[] { "Zeta", "Alpha", "Mike" })
        {
            await store.TryAddAsync(NewProduct(Unique(name), name: $"{name} {marker}"), default);
        }

        var page = await store.SearchAsync(new ProductSearch(null, null, ProductStatusFilter.All, 1, 100), default);

        var mine = page.Items.Where(product => product.Name.EndsWith(marker, StringComparison.Ordinal)).Select(product => product.Name).ToList();
        Assert.Equal(new[] { $"Alpha {marker}", $"Mike {marker}", $"Zeta {marker}" }, mine);
        Assert.True(page.TotalCount >= 3);
    }
}
