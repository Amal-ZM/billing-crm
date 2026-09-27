using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>The invoice list's four dashboard views (spec 011) on real SQL Server.</summary>
public class EfInvoiceStoreSearchFilterTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2030, 6, 15);

    private async Task<Customer> AddCustomerAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync()
    {
        var product = Product.Create("Consulting hour", $"P-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), ProductUnit.Hour, 100m, 15m, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private async Task<Invoice> SeedAsync(InvoiceStatus status, DateOnly? dueDate = null)
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var invoice = Invoice.Create(customer.Id, dueDate ?? Today.AddDays(30), null, Now);
        invoice.AddLine(product, 2m, Now);
        if (status is InvoiceStatus.Sent or InvoiceStatus.Cancelled)
        {
            Assert.True(status == InvoiceStatus.Sent ? invoice.Send(Now) : invoice.Cancel(Now));
        }

        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).AddAsync(invoice, default);
        return invoice;
    }

    private async Task PayAsync(Guid invoiceId, DateOnly paidOn, DateTime recordedAtUtc)
    {
        await using var context = database.CreateContext();
        var store = new EfInvoiceStore(context);
        var tracked = (await store.FindByIdAsync(invoiceId, default))!;
        tracked.RecordPayment(paidOn, 230.00m, PaymentMethod.Cash, null, recordedAtUtc);
        Assert.True(await store.SaveChangesAsync(default));
    }

    private async Task<(List<Guid> Ids, int TotalCount)> IdsAsync(InvoiceSearchFilter filter, int page = 1, int pageSize = 100)
    {
        await using var context = database.CreateContext();
        var result = await new EfInvoiceStore(context).SearchAsync(filter, Today, page, pageSize, default);
        return (result.Items.Select(row => row.Id).ToList(), result.TotalCount);
    }

    [Fact]
    public async Task Status_Draft_returns_only_Draft_invoices()
    {
        var draft = await SeedAsync(InvoiceStatus.Draft);
        var sent = await SeedAsync(InvoiceStatus.Sent);

        var (ids, _) = await IdsAsync(new InvoiceSearchFilter(InvoiceStatus.Draft, false, false));

        Assert.Contains(draft.Id, ids);
        Assert.DoesNotContain(sent.Id, ids);
    }

    [Fact]
    public async Task Status_Sent_with_overdue_true_returns_only_overdue_Sent_invoices()
    {
        var onTime = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(1));
        var overdue = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(-1));

        var (ids, _) = await IdsAsync(new InvoiceSearchFilter(InvoiceStatus.Sent, true, false));

        Assert.Contains(overdue.Id, ids);
        Assert.DoesNotContain(onTime.Id, ids);
    }

    [Fact]
    public async Task Status_Paid_with_paidMonth_true_returns_only_invoices_settled_this_month()
    {
        var thisMonth = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(-10));
        await PayAsync(thisMonth.Id, Today, Now);
        var lastMonth = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(-40));
        await PayAsync(lastMonth.Id, Today.AddMonths(-1), Now.AddMonths(-1));

        var (ids, _) = await IdsAsync(new InvoiceSearchFilter(InvoiceStatus.Paid, false, true));

        Assert.Contains(thisMonth.Id, ids);
        Assert.DoesNotContain(lastMonth.Id, ids);
    }

    [Fact]
    public async Task Paging_within_a_filtered_set_finds_every_seeded_invoice_exactly_once_and_nothing_extra()
    {
        var filter = new InvoiceSearchFilter(InvoiceStatus.Draft, false, false);
        var totalBefore = (await IdsAsync(filter, pageSize: 1)).TotalCount;

        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var seeded = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var invoice = Invoice.Create(customer.Id, Today.AddDays(30), null, Now.AddSeconds(i));
            invoice.AddLine(product, 1m, Now);
            await using var context = database.CreateContext();
            await new EfInvoiceStore(context).AddAsync(invoice, default);
            seeded.Add(invoice.Id);
        }

        var (_, totalAfter) = await IdsAsync(filter, pageSize: 1);
        Assert.Equal(totalBefore + 5, totalAfter);

        // Walk every page (size 2, smaller than the 5 seeded, so they must span at least three pages) and collect every id seen.
        var seen = new List<Guid>();
        for (var page = 1; seen.Count < totalAfter; page++)
        {
            var (items, _) = await IdsAsync(filter, page: page, pageSize: 2);
            if (items.Count == 0)
            {
                break;
            }

            seen.AddRange(items);
        }

        Assert.Equal(totalAfter, seen.Distinct().Count()); // no duplicate and nothing skipped across pages
        Assert.True(seeded.All(seen.Contains)); // every one of this test's own invoices was found somewhere
    }
}
