using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// The Home dashboard's five figures (spec 011) on real SQL Server. The database is shared by the tests in this class, so each test
/// reads the summary <b>before</b> and <b>after</b> its own seeding and asserts the difference, never a raw total (another test's own
/// invoices must never make this one fail).
/// </summary>
public class EfInvoiceStoreSummaryTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
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

    /// <summary>A Draft (grand total 230.00), taken to the given status. `dueDate` matters only for Sent.</summary>
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

    private async Task PayAsync(Guid invoiceId, decimal amount, DateOnly paidOn, DateTime recordedAtUtc)
    {
        await using var context = database.CreateContext();
        var store = new EfInvoiceStore(context);
        var tracked = (await store.FindByIdAsync(invoiceId, default))!;
        tracked.RecordPayment(paidOn, amount, PaymentMethod.Cash, null, recordedAtUtc);
        Assert.True(await store.SaveChangesAsync(default));
    }

    private async Task<DashboardSummaryDto> SummaryAsync()
    {
        await using var context = database.CreateContext();
        return await new EfInvoiceStore(context).GetSummaryAsync(Today, default);
    }

    private static DashboardSummaryDto Delta(DashboardSummaryDto before, DashboardSummaryDto after) => new(
        after.DraftCount - before.DraftCount,
        after.UnpaidCount - before.UnpaidCount,
        after.OverdueCount - before.OverdueCount,
        after.PaidThisMonthCount - before.PaidThisMonthCount,
        after.UnpaidAmount - before.UnpaidAmount);

    [Fact]
    public async Task Each_figure_counts_only_its_own_bucket()
    {
        var before = await SummaryAsync();

        await SeedAsync(InvoiceStatus.Draft);
        var onTime = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(1));
        var overdue = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(-1));
        await SeedAsync(InvoiceStatus.Cancelled);
        var paidThisMonth = await SeedAsync(InvoiceStatus.Sent, Today.AddDays(-5));
        await PayAsync(paidThisMonth.Id, 230.00m, Today, Now);

        var delta = Delta(before, await SummaryAsync());

        Assert.Equal(1, delta.DraftCount);
        Assert.Equal(2, delta.UnpaidCount); // onTime + overdue (not the Paid one, not the Cancelled one)
        Assert.Equal(1, delta.OverdueCount);
        Assert.Equal(1, delta.PaidThisMonthCount);
        Assert.Equal(onTime.Remaining + overdue.Remaining, delta.UnpaidAmount);
    }

    [Fact]
    public async Task A_calendar_month_boundary_is_exact()
    {
        var before = await SummaryAsync();

        var last = await SeedAsync(InvoiceStatus.Sent, new DateOnly(2030, 5, 20));
        await PayAsync(last.Id, 230.00m, new DateOnly(2030, 5, 31), new DateTime(2030, 5, 31, 23, 59, 0, DateTimeKind.Utc));
        var first = await SeedAsync(InvoiceStatus.Sent, new DateOnly(2030, 6, 20));
        await PayAsync(first.Id, 230.00m, new DateOnly(2030, 6, 1), new DateTime(2030, 6, 1, 0, 1, 0, DateTimeKind.Utc));

        // Both invoices' due dates (5-20, 6-20) are in the past relative to Today (6-15) or not; the point under test is only which
        // one's settling payment lands in Today's month, so only PaidThisMonthCount is asserted here.
        Assert.Equal(1, Delta(before, await SummaryAsync()).PaidThisMonthCount);
    }

    [Fact]
    public async Task A_part_payment_two_months_before_the_settling_payment_counts_by_the_settling_payments_own_month()
    {
        var before = await SummaryAsync();

        var invoice = await SeedAsync(InvoiceStatus.Sent, new DateOnly(2030, 7, 1));
        await PayAsync(invoice.Id, 100.00m, new DateOnly(2030, 4, 10), new DateTime(2030, 4, 10, 9, 0, 0, DateTimeKind.Utc));
        await PayAsync(invoice.Id, 130.00m, new DateOnly(2030, 6, 10), new DateTime(2030, 6, 10, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, Delta(before, await SummaryAsync()).PaidThisMonthCount);
    }
}
