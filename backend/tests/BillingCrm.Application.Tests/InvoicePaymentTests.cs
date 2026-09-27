using System.Reflection;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>Payments on the invoice aggregate (spec 008): recording, settling to Paid, the balance, and what stays legal.</summary>
public class InvoicePaymentTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SentAt = Now.AddHours(3);
    private static readonly DateTime PaidAt = Now.AddDays(1);
    private static readonly DateOnly Due = new(2030, 7, 1);
    private static readonly DateOnly PayDate = new(2030, 6, 2);

    private static Product Consulting() => Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100.00m, 15m, Now);

    /// <summary>A Draft for 230.00 (consulting x 2, 15% VAT).</summary>
    private static Invoice Draft()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, "Net 30", Now);
        invoice.AddLine(Consulting(), 2m, Now);
        return invoice;
    }

    private static Invoice Sent()
    {
        var invoice = Draft();
        Assert.True(invoice.Send(SentAt));
        return invoice;
    }

    private static Invoice InStatus(InvoiceStatus status)
    {
        var invoice = Draft();
        switch (status)
        {
            case InvoiceStatus.Sent:
                Assert.True(invoice.Send(SentAt));
                break;
            case InvoiceStatus.Paid:
                Assert.True(invoice.Send(SentAt));
                invoice.RecordPayment(PayDate, 230.00m, PaymentMethod.Bank, null, PaidAt);
                break;
            case InvoiceStatus.Cancelled:
                Assert.True(invoice.Cancel(SentAt));
                break;
        }

        return invoice;
    }

    private static void Force(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    // ---- recording ----

    [Fact]
    public void A_payment_on_a_Sent_invoice_is_added_with_everything_it_was_given()
    {
        var invoice = Sent();

        var payment = invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, "First half", PaidAt);

        Assert.Same(payment, Assert.Single(invoice.Payments));
        Assert.Equal(invoice.Id, payment.InvoiceId);
        Assert.Equal(PayDate, payment.PaidOn);
        Assert.Equal(100.00m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal("First half", payment.Note);
        Assert.Equal(PaidAt, payment.RecordedAtUtc);
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(PaidAt, invoice.UpdatedAtUtc);
    }

    [Fact]
    public void A_part_payment_keeps_the_invoice_Sent_with_the_rest_remaining()
    {
        var invoice = Sent();

        invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, null, PaidAt);

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(100.00m, invoice.Paid);
        Assert.Equal(130.00m, invoice.Remaining);
        Assert.True(invoice.AcceptsPayment);
    }

    [Fact]
    public void The_payment_that_brings_the_remaining_to_zero_makes_the_invoice_Paid()
    {
        var invoice = Sent();
        invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, null, PaidAt);

        invoice.RecordPayment(PayDate, 130.00m, PaymentMethod.Bank, null, PaidAt.AddHours(1));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(230.00m, invoice.Paid);
        Assert.Equal(0.00m, invoice.Remaining);
        Assert.False(invoice.AcceptsPayment);
        Assert.Equal(PaidAt.AddHours(1), invoice.UpdatedAtUtc);
        Assert.Equal(2, invoice.Payments.Count);
    }

    [Fact]
    public void Paying_the_whole_amount_at_once_makes_the_invoice_Paid()
    {
        var invoice = Sent();

        invoice.RecordPayment(PayDate, 230.00m, PaymentMethod.Other, null, PaidAt);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void One_halala_short_is_still_Sent_and_the_last_halala_settles_it()
    {
        var invoice = Sent();
        invoice.RecordPayment(PayDate, 229.99m, PaymentMethod.Cash, null, PaidAt);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(0.01m, invoice.Remaining);

        invoice.RecordPayment(PayDate, 0.01m, PaymentMethod.Cash, null, PaidAt);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0.00m, invoice.Remaining);
    }

    [Fact]
    public void Many_small_payments_settle_exactly_and_never_earlier()
    {
        var invoice = Sent();
        for (var i = 0; i < 22; i++)
        {
            invoice.RecordPayment(PayDate, 10.00m, PaymentMethod.Cash, null, PaidAt);
        }

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(10.00m, invoice.Remaining);

        invoice.RecordPayment(PayDate, 10.00m, PaymentMethod.Cash, null, PaidAt);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0.00m, invoice.Remaining);
    }

    [Fact]
    public void An_overdue_looking_Sent_invoice_takes_a_payment_and_once_settled_is_no_longer_overdue()
    {
        var invoice = Sent();
        var afterDue = Due.AddDays(5);
        Assert.True(invoice.IsOverdue(afterDue));
        invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, null, PaidAt);
        Assert.True(invoice.IsOverdue(afterDue));
        Assert.True(invoice.AcceptsPayment);

        invoice.RecordPayment(PayDate, 130.00m, PaymentMethod.Cash, null, PaidAt);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.False(invoice.IsOverdue(afterDue));
    }

    // ---- what must throw (the service refuses first; reaching these is a bug) ----

    [Fact]
    public void One_halala_more_than_the_remaining_throws_and_changes_nothing()
    {
        var invoice = Sent();
        invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, null, PaidAt);

        Assert.Throws<InvalidOperationException>(() => invoice.RecordPayment(PayDate, 130.01m, PaymentMethod.Cash, null, PaidAt.AddHours(1)));

        Assert.Single(invoice.Payments);
        Assert.Equal(130.00m, invoice.Remaining);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(PaidAt, invoice.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("-1.00")]
    [InlineData("-0.01")]
    public void A_zero_or_negative_amount_throws_and_adds_nothing(string amount)
    {
        var invoice = Sent();

        Assert.Throws<InvalidOperationException>(() => invoice.RecordPayment(PayDate, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), PaymentMethod.Cash, null, PaidAt));

        Assert.Empty(invoice.Payments);
        Assert.Equal(SentAt, invoice.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void A_payment_on_an_invoice_that_is_not_Sent_throws_and_adds_nothing(InvoiceStatus status)
    {
        var invoice = InStatus(status);
        var before = (invoice.Payments.Count, invoice.Status, invoice.UpdatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => invoice.RecordPayment(PayDate, 10.00m, PaymentMethod.Cash, null, PaidAt.AddDays(1)));

        Assert.Equal(before, (invoice.Payments.Count, invoice.Status, invoice.UpdatedAtUtc));
    }

    // ---- the figures ----

    [Fact]
    public void A_Sent_invoice_with_no_payments_shows_nothing_paid_and_the_whole_total_remaining()
    {
        var invoice = Sent();

        Assert.Equal(0.00m, invoice.Paid);
        Assert.Equal(230.00m, invoice.Remaining);
        Assert.Empty(invoice.Payments);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent, true)]
    [InlineData(InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void An_invoice_accepts_a_payment_only_when_it_is_Sent_with_something_remaining(InvoiceStatus status, bool expected)
    {
        Assert.Equal(expected, InStatus(status).AcceptsPayment);
    }

    [Fact]
    public void A_Sent_invoice_with_nothing_remaining_does_not_accept_a_payment()
    {
        // A grand total of 0.00 (a free line): there is nothing to pay, so nothing is accepted, and the invoice stays Sent.
        var invoice = Invoice.Create(Guid.NewGuid(), Due, null, Now);
        invoice.AddLine(Product.Create("Free", "FREE-1", ProductUnit.Item, 0.00m, 15m, Now), 1m, Now);
        Assert.True(invoice.Send(SentAt));

        Assert.Equal(0.00m, invoice.Remaining);
        Assert.False(invoice.AcceptsPayment);
        Assert.Throws<InvalidOperationException>(() => invoice.RecordPayment(PayDate, 0.01m, PaymentMethod.Cash, null, PaidAt));
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public void A_Paid_invoice_is_not_overdue_and_is_read_only()
    {
        var invoice = InStatus(InvoiceStatus.Paid);

        Assert.False(invoice.IsOverdue(Due.AddDays(30)));
        Assert.False(invoice.IsEditable);
        Assert.Throws<InvalidOperationException>(() => invoice.SetLineQuantity(invoice.Lines[0].Id, 5m, PaidAt));
        Assert.False(invoice.Send(PaidAt));
        Assert.False(invoice.Cancel(PaidAt));
    }

    [Fact]
    public void Remaining_uses_the_frozen_grand_total_so_a_changed_line_price_cannot_move_it()
    {
        var invoice = Sent();
        invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, null, PaidAt);

        Force(invoice.Lines[0], nameof(InvoiceLine.UnitPrice), 999.99m);

        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
        Assert.Equal(130.00m, invoice.Remaining);
    }

    [Fact]
    public void Payments_come_back_in_the_order_they_were_recorded_then_by_id()
    {
        var invoice = Sent();
        var second = invoice.RecordPayment(PayDate, 10.00m, PaymentMethod.Cash, "second", PaidAt.AddHours(2));
        var first = invoice.RecordPayment(PayDate, 20.00m, PaymentMethod.Bank, "first", PaidAt.AddHours(1));

        Assert.Equal([first, second], invoice.Payments);
    }

    // ---- allowed changes and cancel ----

    [Fact]
    public void Allowed_transitions_follow_the_status_and_never_list_Paid()
    {
        Assert.Equal([InvoiceStatus.Sent, InvoiceStatus.Cancelled], InStatus(InvoiceStatus.Draft).AllowedTransitions);
        Assert.Equal([InvoiceStatus.Cancelled], InStatus(InvoiceStatus.Sent).AllowedTransitions);
        Assert.Empty(InStatus(InvoiceStatus.Paid).AllowedTransitions);
        Assert.Empty(InStatus(InvoiceStatus.Cancelled).AllowedTransitions);
        foreach (var status in Enum.GetValues<InvoiceStatus>())
        {
            Assert.DoesNotContain(InvoiceStatus.Paid, InStatus(status).AllowedTransitions);
        }
    }

    [Fact]
    public void A_Sent_invoice_with_a_payment_can_no_longer_be_cancelled_and_no_longer_lists_Cancelled()
    {
        var invoice = Sent();
        invoice.RecordPayment(PayDate, 100.00m, PaymentMethod.Cash, null, PaidAt);

        Assert.Empty(invoice.AllowedTransitions);
        Assert.False(invoice.Cancel(PaidAt.AddHours(1)));
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Null(invoice.CancelledAtUtc);
        Assert.Equal(PaidAt, invoice.UpdatedAtUtc);
    }

    [Fact]
    public void A_Sent_invoice_with_no_payment_and_a_Draft_can_still_be_cancelled()
    {
        var sent = Sent();
        var draft = Draft();

        Assert.True(sent.Cancel(PaidAt));
        Assert.True(draft.Cancel(PaidAt));
        Assert.Equal(InvoiceStatus.Cancelled, sent.Status);
        Assert.Equal(InvoiceStatus.Cancelled, draft.Status);
    }

    [Fact]
    public void The_rules_table_has_Sent_to_Paid_and_not_Draft_to_Paid()
    {
        // A payment on a Draft throws (see above) and the table has no Draft to Paid row.
        Assert.False(InvoiceStatusRules.CanChange(InvoiceStatus.Draft, InvoiceStatus.Paid));
        Assert.True(InvoiceStatusRules.CanChange(InvoiceStatus.Sent, InvoiceStatus.Paid));
    }
}
