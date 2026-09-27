using System.Diagnostics;
using BillingCrm.Application.Products;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Products;
using Xunit.Abstractions;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Spec SC-006: with 5,000 products, the first page of a search comes back in under 1 second.</summary>
public class EfProductStorePerformanceTests(LocalDbFixture database, ITestOutputHelper output) : IClassFixture<LocalDbFixture>
{
    private const int ProductCount = 5_000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private async Task SeedAsync()
    {
        var now = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var units = Enum.GetValues<ProductUnit>();
        for (var batch = 0; batch < ProductCount / 1_000; batch++)
        {
            await using var context = database.CreateContext();
            for (var i = 0; i < 1_000; i++)
            {
                var n = batch * 1_000 + i;
                var name = n % 3 == 0 ? $"خدمة رقم {n:00000}" : $"Product {n:00000} Service";
                var product = Product.Create(name, $"SKU-{n:00000}", units[n % units.Length], 10m + n % 500, 15m, now);
                if (n % 7 == 0)
                {
                    product.SetActive(false, now);
                }

                context.Products.Add(product);
            }

            await context.SaveChangesAsync();
        }
    }

    private async Task<TimeSpan> TimeAsync(ProductSearch search)
    {
        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        var watch = Stopwatch.StartNew();
        var result = await store.SearchAsync(search, default);
        watch.Stop();
        Assert.True(result.Items.Count > 0, $"search {search} returned no rows (total {result.TotalCount})");
        return watch.Elapsed;
    }

    [Fact]
    public async Task The_first_page_of_a_search_over_5000_products_returns_in_under_a_second()
    {
        await SeedAsync();
        await using (var context = database.CreateContext())
        {
            var everything = await new EfProductStore(context).SearchAsync(new ProductSearch(null, null, ProductStatusFilter.All, 1, 1), default);
            Assert.Equal(ProductCount, everything.TotalCount);
        }

        // One warm-up so connection setup and query compilation are not counted.
        await TimeAsync(new ProductSearch("product", null, ProductStatusFilter.Active, 1, 20));

        var searches = new (string Label, ProductSearch Search)[]
        {
            ("name fragment", new ProductSearch("product 0012", null, ProductStatusFilter.Active, 1, 20)),
            ("sku fragment", new ProductSearch("sku-00456", null, ProductStatusFilter.Active, 1, 20)),
            ("arabic fragment", new ProductSearch("خدمة رقم 001", null, ProductStatusFilter.Active, 1, 20)),
            ("no text, first page", new ProductSearch(null, null, ProductStatusFilter.Active, 1, 20)),
            ("unit only", new ProductSearch(null, ProductUnit.Service, ProductStatusFilter.Active, 1, 20)),
            ("status all", new ProductSearch(null, null, ProductStatusFilter.All, 1, 20)),
            ("inactive only", new ProductSearch(null, null, ProductStatusFilter.Inactive, 1, 20)),
            ("text + unit + status", new ProductSearch("product", ProductUnit.Item, ProductStatusFilter.Active, 1, 20)),
        };

        foreach (var (label, search) in searches)
        {
            var elapsed = await TimeAsync(search);
            output.WriteLine($"{label}: {elapsed.TotalMilliseconds:0} ms");
            Assert.True(elapsed < Limit, $"{label} took {elapsed.TotalMilliseconds:0} ms (limit {Limit.TotalMilliseconds:0} ms)");
        }
    }
}
