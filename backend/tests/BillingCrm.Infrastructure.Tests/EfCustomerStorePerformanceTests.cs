using System.Diagnostics;
using BillingCrm.Application.Customers;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using Xunit.Abstractions;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Spec SC-003: with 10,000 customers, the first page of a search comes back in under 1 second.</summary>
public class EfCustomerStorePerformanceTests(LocalDbFixture database, ITestOutputHelper output) : IClassFixture<LocalDbFixture>
{
    private const int CustomerCount = 10_000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private async Task SeedAsync()
    {
        var now = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        for (var batch = 0; batch < CustomerCount / 1_000; batch++)
        {
            await using var context = database.CreateContext();
            for (var i = 0; i < 1_000; i++)
            {
                var n = batch * 1_000 + i;
                var name = n % 3 == 0 ? $"شركة رقم {n:00000}" : $"Customer {n:00000} Trading";
                var customer = Customer.Create(
                    name,
                    n % 2 == 0 ? CustomerType.Company : CustomerType.Individual,
                    $"customer{n:00000}@example.test",
                    $"05{n:00000000}",
                    n % 5 == 0 ? $"VAT-{n:00000}" : null,
                    "Somewhere",
                    now);
                if (n % 7 == 0)
                {
                    customer.SetActive(false, now);
                }

                context.Customers.Add(customer);
            }

            await context.SaveChangesAsync();
        }
    }

    private async Task<TimeSpan> TimeAsync(CustomerSearch search)
    {
        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        var watch = Stopwatch.StartNew();
        var result = await store.SearchAsync(search, default);
        watch.Stop();
        Assert.True(result.Items.Count > 0, $"search {search} returned no rows (total {result.TotalCount})");
        return watch.Elapsed;
    }

    [Fact]
    public async Task The_first_page_of_a_search_over_10000_customers_returns_in_under_a_second()
    {
        await SeedAsync();
        await using (var context = database.CreateContext())
        {
            var everything = await new EfCustomerStore(context).SearchAsync(new CustomerSearch(null, null, null, 1, 1), default);
            Assert.Equal(CustomerCount, everything.TotalCount);
        }

        // One warm-up so connection setup and query compilation are not counted.
        await TimeAsync(new CustomerSearch("customer", null, null, 1, 20));

        var searches = new (string Label, CustomerSearch Search)[]
        {
            ("name fragment", new CustomerSearch("customer 0012", null, null, 1, 20)),
            ("email fragment", new CustomerSearch("customer00456@", null, null, 1, 20)),
            ("phone fragment", new CustomerSearch("0500000789", null, null, 1, 20)),
            ("vat fragment", new CustomerSearch("VAT-00500", null, null, 1, 20)),
            ("arabic fragment", new CustomerSearch("شركة رقم 001", null, null, 1, 20)),
            ("no text, first page", new CustomerSearch(null, null, null, 1, 20)),
            ("filters only", new CustomerSearch(null, CustomerType.Individual, true, 1, 20)),
            ("text + filters", new CustomerSearch("customer", CustomerType.Company, false, 1, 20)),
        };

        foreach (var (label, search) in searches)
        {
            var elapsed = await TimeAsync(search);
            output.WriteLine($"{label}: {elapsed.TotalMilliseconds:0} ms");
            Assert.True(elapsed < Limit, $"{label} took {elapsed.TotalMilliseconds:0} ms (limit {Limit.TotalMilliseconds:0} ms)");
        }
    }
}
