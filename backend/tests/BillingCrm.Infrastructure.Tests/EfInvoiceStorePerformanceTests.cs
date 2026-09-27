using System.Diagnostics;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using Xunit.Abstractions;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Spec SC-006: with 5,000 invoices, the first page of the list comes back in under 1 second.</summary>
public class EfInvoiceStorePerformanceTests(LocalDbFixture database, ITestOutputHelper output) : IClassFixture<LocalDbFixture>
{
    private const int InvoiceCount = 5_000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private async Task SeedAsync()
    {
        var now = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        // A few hundred customers and products, so the list's customer join has real work to do.
        var customers = new List<Customer>();
        var products = new List<Product>();
        await using (var context = database.CreateContext())
        {
            for (var i = 0; i < 300; i++)
            {
                var customer = Customer.Create(
                    i % 3 == 0 ? $"شركة رقم {i:000}" : $"Customer {i:000} Trading",
                    CustomerType.Company,
                    $"customer{i:000}@example.test",
                    $"05{i:00000000}",
                    null,
                    "Riyadh",
                    now);
                customers.Add(customer);
                context.Customers.Add(customer);
                var product = Product.Create($"Product {i:000}", $"SKU-{i:000}", ProductUnit.Item, 10m + i, 15m, now);
                products.Add(product);
                context.Products.Add(product);
            }

            await context.SaveChangesAsync();
        }

        for (var batch = 0; batch < InvoiceCount / 500; batch++)
        {
            await using var context = database.CreateContext();
            for (var i = 0; i < 500; i++)
            {
                var n = batch * 500 + i;
                var created = now.AddSeconds(n);
                var invoice = Invoice.Create(customers[n % customers.Count].Id, new DateOnly(2030, 7, 1), n % 4 == 0 ? "Net 30" : null, created);
                invoice.AddLine(products[n % products.Count], 1m + n % 5, created);
                context.Invoices.Add(invoice);
            }

            await context.SaveChangesAsync();
        }
    }

    private async Task<TimeSpan> TimeAsync(int page, int pageSize)
    {
        await using var context = database.CreateContext();
        var store = new EfInvoiceStore(context);
        var watch = Stopwatch.StartNew();
        var result = await store.SearchAsync(InvoiceSearchFilter.None, DateOnly.FromDateTime(DateTime.UtcNow), page, pageSize, default);
        watch.Stop();
        Assert.True(result.Items.Count > 0, $"page {page} of {pageSize} returned no rows (total {result.TotalCount})");
        return watch.Elapsed;
    }

    [Fact]
    public async Task The_first_page_of_the_list_over_5000_invoices_returns_in_under_a_second()
    {
        await SeedAsync();
        await using (var context = database.CreateContext())
        {
            var everything = await new EfInvoiceStore(context).SearchAsync(InvoiceSearchFilter.None, DateOnly.FromDateTime(DateTime.UtcNow), 1, 1, default);
            Assert.Equal(InvoiceCount, everything.TotalCount);
        }

        // One warm-up so connection setup and query compilation are not counted.
        await TimeAsync(1, 20);

        var pages = new (string Label, int Page, int Size)[]
        {
            ("first page, 20", 1, 20),
            ("first page, 100", 1, 100),
            ("second page, 20", 2, 20),
            ("middle page, 50", 50, 50),
            ("last full page, 100", 50, 100),
        };

        foreach (var (label, page, size) in pages)
        {
            var elapsed = await TimeAsync(page, size);
            output.WriteLine($"{label}: {elapsed.TotalMilliseconds:0} ms");
            Assert.True(elapsed < Limit, $"{label} took {elapsed.TotalMilliseconds:0} ms (limit {Limit.TotalMilliseconds:0} ms)");
        }
    }
}
