using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>Recording payments through the service (spec 008), with fakes: the worked example, the order of checks and the lost race.</summary>
public class InvoiceServicePaymentTests
{
    // 2030-06-01 10:00 UTC is 13:00 in Saudi Arabia, the same calendar day.
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly FakeCurrentUser _user = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Product _consulting;

    public InvoiceServicePaymentTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Start.UtcDateTime);
        _products.Seed(_consulting);
    }

    /// <summary>A stored invoice (ABC, consulting x 2, grand total 230.00) that was sent at the start, with the given due date.</summary>
    private Invoice SeededSent(string dueDate = "2030-07-01")
    {
        var invoice = Invoice.Create(_abc.Id, DateOnly.Parse(dueDate), "Net 30", Start.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        Assert.True(invoice.Send(Start.UtcDateTime));
        _invoices.Seed(invoice);
        return invoice;
    }

    private static PaymentInput Pay(decimal amount, string method = "Cash", string? note = null, string date = "2030-06-01") =>
        new(date, amount, method, note);

    private static InvoiceDto Ok(Result<InvoiceDto> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    // ---- the worked example ----

    [Fact]
    public async Task Paying_100_then_130_on_a_grand_total_of_230_ends_Paid_with_nothing_remaining()
    {
        var invoice = SeededSent();

        var first = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(100.00m), CancellationToken.None));

        Assert.Equal("Sent", first.Status);
        Assert.Equal(230.00m, first.Totals.GrandTotal);
        Assert.Equal(100.00m, first.Paid);
        Assert.Equal(130.00m, first.Remaining);
        Assert.True(first.AcceptsPayment);

        _time.Advance(TimeSpan.FromMinutes(5));
        var second = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(130.00m, "Bank"), CancellationToken.None));

        Assert.Equal("Paid", second.Status);
        Assert.Equal(230.00m, second.Paid);
        Assert.Equal(0.00m, second.Remaining);
        Assert.False(second.AcceptsPayment);
        Assert.False(second.IsOverdue);
        Assert.Empty(second.AllowedTransitions);
        Assert.Equal([100.00m, 130.00m], second.Payments.Select(payment => payment.Amount));
        Assert.Equal(2, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task An_Overdue_invoice_paid_in_part_stays_Overdue_and_paid_in_full_is_Paid_and_not_Overdue()
    {
        var invoice = SeededSent(dueDate: "2030-06-05");
        _time.Advance(TimeSpan.FromDays(10));
        var later = "2030-06-11";

        var part = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(100.00m, date: later), CancellationToken.None));

        Assert.Equal("Sent", part.Status);
        Assert.True(part.IsOverdue);
        Assert.True(part.AcceptsPayment);
        Assert.Equal(130.00m, part.Remaining);

        var settled = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(130.00m, date: later), CancellationToken.None));

        Assert.Equal("Paid", settled.Status);
        Assert.False(settled.IsOverdue);
    }

    // ---- what is saved ----

    [Fact]
    public async Task The_payment_is_saved_with_its_date_amount_method_note_and_the_time_it_was_recorded()
    {
        var invoice = SeededSent();
        _time.Advance(TimeSpan.FromHours(2));

        var result = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(100.10m, "Other", "First half", "2030-06-01"), CancellationToken.None));

        var payment = Assert.Single(result.Payments);
        Assert.Equal("2030-06-01", payment.Date);
        Assert.Equal(100.10m, payment.Amount);
        Assert.Equal("Other", payment.Method);
        Assert.Equal("First half", payment.Note);
        Assert.Equal(Start.UtcDateTime.AddHours(2), payment.RecordedAtUtc);
        Assert.Equal(payment.Id, Assert.Single(invoice.Payments).Id);
    }

    [Fact]
    public async Task The_note_is_trimmed_and_an_empty_one_is_saved_as_none()
    {
        var invoice = SeededSent();

        var withNote = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, note: "  thanks  "), CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(1));
        var withoutNote = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, note: "   "), CancellationToken.None));

        Assert.Equal("thanks", withNote.Payments[0].Note);
        Assert.Null(withoutNote.Payments[1].Note);
    }

    [Fact]
    public async Task The_payment_is_saved_exactly_once()
    {
        var invoice = SeededSent();

        Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(50.00m), CancellationToken.None));

        Assert.Equal(1, _invoices.SaveChangesCalls);
        Assert.Single(invoice.Payments);
    }

    [Fact]
    public async Task The_result_carries_the_invoices_own_customer_and_number()
    {
        var invoice = SeededSent();

        var result = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(50.00m), CancellationToken.None));

        Assert.Equal(invoice.Id, result.Id);
        Assert.Equal("ABC", result.Customer.Name);
        Assert.Equal(InvoiceNumber.Format(invoice.Number), result.Number);
    }

    [Fact]
    public async Task A_repriced_product_does_not_change_what_remains()
    {
        var invoice = SeededSent();
        Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(100.00m), CancellationToken.None));
        typeof(InvoiceLine).GetProperty(nameof(InvoiceLine.UnitPrice))!.SetValue(invoice.Lines[0], 999.99m);

        var result = Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(30.00m), CancellationToken.None));

        Assert.Equal(230.00m, result.Totals.GrandTotal);
        Assert.Equal(130.00m, result.Paid);
        Assert.Equal(100.00m, result.Remaining);
    }

    // ---- not found and the lost race ----

    [Fact]
    public async Task An_unknown_invoice_is_not_found_and_nothing_is_saved()
    {
        var result = await _sut.RecordPaymentAsync(Guid.NewGuid(), Pay(10.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal("invoice_not_found", result.Error.Code);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task A_save_that_loses_a_race_is_a_conflict_changed_by_someone_else()
    {
        var invoice = SeededSent();
        _invoices.SaveLosesRace = true;

        var result = await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal("invoice_changed", result.Error.Code);
        Assert.Equal(1, _invoices.SaveChangesCalls);
    }
}
