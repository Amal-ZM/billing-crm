using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The Home dashboard's five figures (spec 011), through the service with fakes: what each counts, and what it never counts.</summary>
public class InvoiceServiceSummaryTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Product _consulting;

    public InvoiceServiceSummaryTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, new FakeCurrentUser());
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now.UtcDateTime);
        _products.Seed(_consulting);
    }

    private Invoice Seed(InvoiceStatus status, string dueDate = "2030-07-01", decimal quantity = 2m)
    {
        var invoice = Invoice.Create(_abc.Id, DateOnly.Parse(dueDate), null, Now.UtcDateTime);
        invoice.AddLine(_consulting, quantity, Now.UtcDateTime);
        switch (status)
        {
            case InvoiceStatus.Sent:
                Assert.True(invoice.Send(Now.UtcDateTime));
                break;
            case InvoiceStatus.Cancelled:
                Assert.True(invoice.Cancel(Now.UtcDateTime));
                break;
        }

        _invoices.Seed(invoice);
        return invoice;
    }

    private Invoice SeedSent(string dueDate) => Seed(InvoiceStatus.Sent, dueDate);

    /// <summary>A Sent invoice (grand total 230.00) paid in full by one payment on the given date.</summary>
    private Invoice SeedPaidOn(string paidOn, DateTime? recordedAtUtc = null)
    {
        var invoice = SeedSent("2030-07-01");
        invoice.RecordPayment(DateOnly.Parse(paidOn), 230.00m, PaymentMethod.Cash, null, recordedAtUtc ?? Now.UtcDateTime);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        return invoice;
    }

    /// <summary>A Sent invoice (grand total 230.00) paid in two instalments: a part payment, then the settling one.</summary>
    private Invoice SeedPaidInTwoParts(string partPaidOn, DateTime partRecordedAtUtc, string settlingPaidOn, DateTime settlingRecordedAtUtc)
    {
        var invoice = SeedSent("2030-07-01");
        invoice.RecordPayment(DateOnly.Parse(partPaidOn), 100.00m, PaymentMethod.Cash, null, partRecordedAtUtc);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        invoice.RecordPayment(DateOnly.Parse(settlingPaidOn), 130.00m, PaymentMethod.Bank, null, settlingRecordedAtUtc);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        return invoice;
    }

    private async Task<DashboardSummaryDto> SummaryAsync()
    {
        var result = await _sut.GetSummaryAsync(CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    // ---- an empty store ----

    [Fact]
    public async Task An_empty_store_gives_every_count_zero_and_the_amount_zero()
    {
        var summary = await SummaryAsync();

        Assert.Equal(0, summary.DraftCount);
        Assert.Equal(0, summary.UnpaidCount);
        Assert.Equal(0, summary.OverdueCount);
        Assert.Equal(0, summary.PaidThisMonthCount);
        Assert.Equal(0m, summary.UnpaidAmount);
    }

    // ---- each figure ----

    [Fact]
    public async Task Draft_count_is_the_number_of_Draft_invoices()
    {
        Seed(InvoiceStatus.Draft);
        Seed(InvoiceStatus.Draft);
        SeedSent("2030-08-01");

        Assert.Equal(2, (await SummaryAsync()).DraftCount);
    }

    [Fact]
    public async Task Unpaid_count_is_every_Sent_invoice_on_time_and_overdue_alike()
    {
        SeedSent("2030-08-01"); // on-time (today is 2030-06-15)
        SeedSent("2030-06-01"); // overdue
        Seed(InvoiceStatus.Draft);

        Assert.Equal(2, (await SummaryAsync()).UnpaidCount);
    }

    [Fact]
    public async Task Overdue_count_is_only_the_Sent_invoices_whose_due_date_has_passed()
    {
        SeedSent("2030-08-01");
        SeedSent("2030-06-01");
        SeedSent("2030-06-14"); // due yesterday: overdue
        SeedSent("2030-06-15"); // due today: not overdue (today itself is not "before today")

        Assert.Equal(2, (await SummaryAsync()).OverdueCount);
    }

    [Fact]
    public async Task Paid_this_month_counts_by_the_settling_payments_own_date_not_an_earlier_part_payments()
    {
        SeedPaidOn("2030-06-10"); // settled this month
        SeedPaidInTwoParts("2030-05-20", Now.UtcDateTime.AddDays(-20), "2030-06-05", Now.UtcDateTime.AddDays(-5)); // part last month, settled this month
        SeedPaidOn("2030-05-31"); // settled last month: does not count

        Assert.Equal(2, (await SummaryAsync()).PaidThisMonthCount);
    }

    [Fact]
    public async Task Cancelled_invoices_affect_no_figure()
    {
        Seed(InvoiceStatus.Cancelled);

        var summary = await SummaryAsync();

        Assert.Equal(0, summary.DraftCount);
        Assert.Equal(0, summary.UnpaidCount);
        Assert.Equal(0, summary.OverdueCount);
        Assert.Equal(0, summary.PaidThisMonthCount);
        Assert.Equal(0m, summary.UnpaidAmount);
    }

    // ---- the unpaid amount sum ----

    [Fact]
    public async Task The_unpaid_amount_sum_is_the_Sent_invoices_own_remaining_balances_added_together()
    {
        var onTime = SeedSent("2030-08-01");
        var overdue = SeedSent("2030-06-01");
        overdue.RecordPayment(DateOnly.Parse("2030-06-10"), 30.00m, PaymentMethod.Cash, null, Now.UtcDateTime);

        var summary = await SummaryAsync();

        Assert.Equal(onTime.Remaining + overdue.Remaining, summary.UnpaidAmount);
        Assert.Equal(430.00m, summary.UnpaidAmount); // 230.00 + (230.00 - 30.00)
    }

    [Fact]
    public async Task The_unpaid_amount_sum_excludes_Draft_Paid_and_Cancelled_invoices()
    {
        Seed(InvoiceStatus.Draft);
        SeedPaidOn("2030-06-10");
        Seed(InvoiceStatus.Cancelled);
        var sent = SeedSent("2030-08-01");

        Assert.Equal(sent.Remaining, (await SummaryAsync()).UnpaidAmount);
    }

    // ---- reading the summary changes nothing ----

    [Fact]
    public async Task Reading_the_summary_saves_nothing_and_adds_no_pending_history()
    {
        SeedSent("2030-08-01");
        var saves = _invoices.SaveChangesCalls;

        await SummaryAsync();
        await SummaryAsync();

        Assert.Equal(saves, _invoices.SaveChangesCalls);
        Assert.Equal(0, _invoices.PendingHistoryCount);
    }

    // ---- the worked example (request's own scenario) ----

    [Fact]
    public async Task One_overdue_and_one_paid_this_month_show_1_and_1_and_the_unpaid_sum_excludes_the_paid_invoice()
    {
        var overdue = SeedSent("2030-06-01");
        SeedPaidOn("2030-06-10");

        var summary = await SummaryAsync();

        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(1, summary.PaidThisMonthCount);
        Assert.Equal(overdue.Remaining, summary.UnpaidAmount);
    }
}
