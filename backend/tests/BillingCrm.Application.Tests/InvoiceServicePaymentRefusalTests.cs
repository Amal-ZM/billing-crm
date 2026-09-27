using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>Every payment that must be refused, and the cancel rule for an invoice with payments (spec 008, US2), with fakes.</summary>
public class InvoiceServicePaymentRefusalTests
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

    public InvoiceServicePaymentRefusalTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Start.UtcDateTime);
        _products.Seed(_consulting);
    }

    private Invoice Seeded(InvoiceStatus status, DateTime? sentAt = null)
    {
        var invoice = Invoice.Create(_abc.Id, new DateOnly(2030, 7, 1), "Net 30", Start.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        switch (status)
        {
            case InvoiceStatus.Sent:
                Assert.True(invoice.Send(sentAt ?? Start.UtcDateTime));
                break;
            case InvoiceStatus.Paid:
                Assert.True(invoice.Send(Start.UtcDateTime));
                invoice.RecordPayment(new DateOnly(2030, 6, 1), 230.00m, PaymentMethod.Bank, null, Start.UtcDateTime);
                break;
            case InvoiceStatus.Cancelled:
                Assert.True(invoice.Cancel(Start.UtcDateTime));
                break;
        }

        _invoices.Seed(invoice);
        return invoice;
    }

    private static PaymentInput Pay(decimal? amount, string? method = "Cash", string? note = null, string? date = "2030-06-01") =>
        new(date, amount, method, note);

    private static Error ErrorOf(Result<InvoiceDto> result)
    {
        Assert.True(result.IsFailure);
        return result.Error!;
    }

    private static IReadOnlyDictionary<string, string[]> FieldErrors(Result<InvoiceDto> result)
    {
        var error = ErrorOf(result);
        Assert.Equal(ErrorKind.Validation, error.Kind);
        return error.Errors!;
    }

    // ---- status: only a Sent invoice takes a payment ----

    [Theory]
    [InlineData(InvoiceStatus.Draft, "Draft")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    public async Task A_payment_on_a_Draft_Cancelled_or_Paid_invoice_is_a_conflict_naming_the_status_and_saves_nothing(InvoiceStatus status, string shown)
    {
        var invoice = Seeded(status);
        var before = (invoice.Status, invoice.Payments.Count, invoice.UpdatedAtUtc);

        var error = ErrorOf(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m), CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_payment_not_allowed", error.Code);
        Assert.Equal("Payment not allowed", error.Title);
        Assert.Equal($"An invoice that is {shown} cannot take a payment.", error.Detail);
        Assert.Equal(before, (invoice.Status, invoice.Payments.Count, invoice.UpdatedAtUtc));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task The_status_is_judged_before_the_body_so_an_invalid_payment_on_such_an_invoice_is_still_the_conflict(InvoiceStatus status)
    {
        var invoice = Seeded(status);

        var error = ErrorOf(await _sut.RecordPaymentAsync(invoice.Id, new PaymentInput(null, -5m, "Card", new string('x', 2000)), CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_payment_not_allowed", error.Code);
    }

    // ---- the body ----

    [Fact]
    public async Task An_invalid_body_on_a_Sent_invoice_reports_every_problem_together_and_saves_nothing()
    {
        var invoice = Seeded(InvoiceStatus.Sent);

        var errors = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, new PaymentInput("2030-06-99", 10.005m, "Card", new string('x', 1001)), CancellationToken.None));

        Assert.Equal(["amount", "date", "method", "note"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Empty(invoice.Payments);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task An_amount_one_halala_above_what_remains_is_refused_on_the_amount_naming_what_remains_and_the_exact_remainder_is_accepted()
    {
        var invoice = Seeded(InvoiceStatus.Sent);
        await _sut.RecordPaymentAsync(invoice.Id, Pay(100.00m), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));

        var refused = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(130.01m), CancellationToken.None));

        Assert.Equal(["Amount cannot be more than the remaining 130.00 SAR."], refused["amount"]);
        Assert.Single(invoice.Payments);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);

        var accepted = await _sut.RecordPaymentAsync(invoice.Id, Pay(130.00m), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Equal("Paid", accepted.Value.Status);
        Assert.Equal(0.00m, accepted.Value.Remaining);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.005")]
    [InlineData("0.001")]
    public async Task A_zero_negative_or_three_decimal_amount_is_refused_on_the_amount_and_never_rounded(string amount)
    {
        var invoice = Seeded(InvoiceStatus.Sent);

        var errors = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None));

        Assert.Equal([PaymentValidator.AmountRule], errors["amount"]);
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public async Task A_missing_amount_is_refused_on_the_amount()
    {
        var invoice = Seeded(InvoiceStatus.Sent);

        var errors = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(null), CancellationToken.None));

        Assert.Equal([PaymentValidator.AmountRequired], errors["amount"]);
    }

    [Fact]
    public async Task A_Sent_invoice_with_a_grand_total_of_zero_refuses_any_amount_and_stays_Sent()
    {
        var invoice = Invoice.Create(_abc.Id, new DateOnly(2030, 7, 1), null, Start.UtcDateTime);
        invoice.AddLine(Product.Create("Free", "FREE-1", ProductUnit.Item, 0.00m, 15m, Start.UtcDateTime), 1m, Start.UtcDateTime);
        Assert.True(invoice.Send(Start.UtcDateTime));
        _invoices.Seed(invoice);

        var errors = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(0.01m), CancellationToken.None));

        Assert.Equal(["Amount cannot be more than the remaining 0.00 SAR."], errors["amount"]);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public async Task A_method_must_be_spelled_exactly_and_a_note_may_not_be_too_long()
    {
        var invoice = Seeded(InvoiceStatus.Sent);

        var wrongMethod = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, method: "cash"), CancellationToken.None));
        var longNote = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, note: new string('x', 1001)), CancellationToken.None));

        Assert.Equal([PaymentValidator.MethodRule], wrongMethod["method"]);
        Assert.Equal([PaymentValidator.NoteTooLong], longNote["note"]);
        Assert.Empty(invoice.Payments);
    }

    // ---- dates: the Saudi day, and the invoice's window ----

    [Fact]
    public async Task At_23_59_in_Saudi_Arabia_today_is_still_the_same_day_and_tomorrow_is_the_future()
    {
        var invoice = Seeded(InvoiceStatus.Sent);
        _time.Advance(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(59)); // 2030-06-01 20:59 UTC = 23:59 Saudi

        var tomorrow = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-06-02"), CancellationToken.None));
        var today = await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-06-01"), CancellationToken.None);

        Assert.Equal([PaymentValidator.DateInTheFuture], tomorrow["date"]);
        Assert.True(today.IsSuccess);
    }

    [Fact]
    public async Task One_minute_later_it_is_the_next_day_in_Saudi_Arabia_and_that_date_is_accepted()
    {
        var invoice = Seeded(InvoiceStatus.Sent);
        _time.Advance(TimeSpan.FromHours(11)); // 2030-06-01 21:00 UTC = 2030-06-02 00:00 Saudi

        var nextDay = await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-06-02"), CancellationToken.None);

        Assert.True(nextDay.IsSuccess);
    }

    [Fact]
    public async Task A_date_before_the_day_the_invoice_was_sent_is_refused_and_the_sent_day_is_accepted()
    {
        var invoice = Seeded(InvoiceStatus.Sent);

        var before = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-05-31"), CancellationToken.None));
        var sameDay = await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-06-01"), CancellationToken.None);

        Assert.Equal(["Date cannot be before the invoice was sent (2030-06-01)."], before["date"]);
        Assert.True(sameDay.IsSuccess);
    }

    [Fact]
    public async Task The_sent_day_is_the_Saudi_day_so_an_invoice_sent_at_00_30_Saudi_time_cannot_be_paid_on_the_UTC_day_before()
    {
        // Sent at 2030-06-01 21:30 UTC = 2030-06-02 00:30 in Saudi Arabia.
        var invoice = Seeded(InvoiceStatus.Sent, sentAt: new DateTime(2030, 6, 1, 21, 30, 0, DateTimeKind.Utc));
        _time.Advance(TimeSpan.FromHours(12)); // 22:00 UTC, still 2030-06-02 in Saudi Arabia

        var utcDay = FieldErrors(await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-06-01"), CancellationToken.None));
        var saudiDay = await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m, date: "2030-06-02"), CancellationToken.None);

        Assert.Equal(["Date cannot be before the invoice was sent (2030-06-02)."], utcDay["date"]);
        Assert.True(saudiDay.IsSuccess);
    }

    // ---- cancel: an invoice that has payments cannot be cancelled ----

    [Fact]
    public async Task Cancelling_a_Sent_invoice_that_has_a_payment_is_a_conflict_and_changes_nothing()
    {
        var invoice = Seeded(InvoiceStatus.Sent);
        await _sut.RecordPaymentAsync(invoice.Id, Pay(100.00m), CancellationToken.None);
        var saves = _invoices.SaveChangesCalls;
        var updated = invoice.UpdatedAtUtc;

        var error = ErrorOf(await _sut.CancelAsync(invoice.Id, CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_has_payments", error.Code);
        Assert.Equal("Invoice has payments", error.Title);
        Assert.Equal("An invoice that has payments cannot be cancelled.", error.Detail);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Null(invoice.CancelledAtUtc);
        Assert.Equal(updated, invoice.UpdatedAtUtc);
        Assert.Equal(saves, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task Cancelling_a_late_Sent_invoice_that_has_a_payment_is_refused_the_same_way()
    {
        var invoice = Seeded(InvoiceStatus.Sent);
        await _sut.RecordPaymentAsync(invoice.Id, Pay(10.00m), CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(60));

        var error = ErrorOf(await _sut.CancelAsync(invoice.Id, CancellationToken.None));

        Assert.Equal("invoice_has_payments", error.Code);
    }

    [Fact]
    public async Task A_Sent_invoice_with_no_payment_and_a_Draft_can_still_be_cancelled()
    {
        var sent = Seeded(InvoiceStatus.Sent);
        var draft = Seeded(InvoiceStatus.Draft);

        var sentResult = await _sut.CancelAsync(sent.Id, CancellationToken.None);
        var draftResult = await _sut.CancelAsync(draft.Id, CancellationToken.None);

        Assert.True(sentResult.IsSuccess);
        Assert.Equal("Cancelled", sentResult.Value.Status);
        Assert.True(draftResult.IsSuccess);
        Assert.Equal("Cancelled", draftResult.Value.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public async Task Cancelling_a_Paid_or_Cancelled_invoice_is_still_the_illegal_change_and_that_is_judged_before_the_payments_rule(InvoiceStatus status, string shown)
    {
        var invoice = Seeded(status);

        var error = ErrorOf(await _sut.CancelAsync(invoice.Id, CancellationToken.None));

        Assert.Equal("invoice_status_change_not_allowed", error.Code);
        Assert.Equal($"An invoice that is {shown} cannot be cancelled.", error.Detail);
    }

    // ---- what the response offers ----

    [Fact]
    public async Task The_allowed_transitions_and_acceptsPayment_follow_the_invoice_through_a_payment()
    {
        var invoice = Seeded(InvoiceStatus.Sent);

        var before = (await _sut.GetAsync(invoice.Id, CancellationToken.None)).Value;
        var afterPart = (await _sut.RecordPaymentAsync(invoice.Id, Pay(100.00m), CancellationToken.None)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));
        var afterAll = (await _sut.RecordPaymentAsync(invoice.Id, Pay(130.00m), CancellationToken.None)).Value;

        Assert.Equal(["Cancelled"], before.AllowedTransitions);
        Assert.True(before.AcceptsPayment);
        Assert.Empty(afterPart.AllowedTransitions);
        Assert.True(afterPart.AcceptsPayment);
        Assert.Empty(afterAll.AllowedTransitions);
        Assert.False(afterAll.AcceptsPayment);
        Assert.DoesNotContain("Paid", before.AllowedTransitions);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task A_Draft_or_Cancelled_invoice_reports_no_payment_and_does_not_accept_one(InvoiceStatus status)
    {
        var invoice = Seeded(status);

        var dto = (await _sut.GetAsync(invoice.Id, CancellationToken.None)).Value;

        Assert.False(dto.AcceptsPayment);
        Assert.Equal(0.00m, dto.Paid);
        Assert.Empty(dto.Payments);
    }
}
