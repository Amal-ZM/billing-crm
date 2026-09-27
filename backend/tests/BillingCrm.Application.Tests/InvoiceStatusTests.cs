using System.Reflection;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The status behaviour of the invoice aggregate (spec 007): send, cancel, Overdue, frozen amounts and the edit guard.</summary>
public class InvoiceStatusTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(3);
    private static readonly DateOnly Due = new(2030, 7, 1);

    private static Product Consulting() => Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100.00m, 15m, Now);

    private static Invoice Draft(decimal discount = 0m)
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, "Net 30", Now, discount);
        invoice.AddLine(Consulting(), 2m, Now);
        return invoice;
    }

    private static Invoice InStatus(InvoiceStatus status)
    {
        var invoice = Draft();
        switch (status)
        {
            case InvoiceStatus.Sent:
                Assert.True(invoice.Send(Later));
                break;
            case InvoiceStatus.Paid:
                Assert.True(invoice.Send(Later));
                Force(invoice, nameof(Invoice.Status), InvoiceStatus.Paid);
                break;
            case InvoiceStatus.Cancelled:
                Assert.True(invoice.Cancel(Later));
                break;
        }

        return invoice;
    }

    /// <summary>Sets a private-setter member the way a database row would, for states a test wants without going through the rules.</summary>
    private static void Force(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    // ---- a new invoice ----

    [Fact]
    public void A_new_invoice_is_a_Draft_with_no_sent_or_cancelled_time_and_is_editable()
    {
        var invoice = Draft();

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.SentAtUtc);
        Assert.Null(invoice.CancelledAtUtc);
        Assert.True(invoice.IsEditable);
    }

    // ---- send ----

    [Fact]
    public void Sending_a_Draft_makes_it_Sent_records_the_time_and_changes_nothing_else()
    {
        var invoice = Draft(20.00m);
        var (customer, due, notes, discount, number) = (invoice.CustomerId, invoice.DueDate, invoice.Notes, invoice.Discount, invoice.Number);
        var lineIds = invoice.Lines.Select(line => line.Id).ToList();

        var changed = invoice.Send(Later);

        Assert.True(changed);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(Later, invoice.SentAtUtc);
        Assert.Null(invoice.CancelledAtUtc);
        Assert.Equal(Later, invoice.UpdatedAtUtc);
        Assert.Equal((customer, due, notes, discount, number), (invoice.CustomerId, invoice.DueDate, invoice.Notes, invoice.Discount, invoice.Number));
        Assert.Equal(lineIds, invoice.Lines.Select(line => line.Id));
        Assert.False(invoice.IsEditable);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void Sending_an_invoice_that_is_not_a_Draft_changes_nothing_and_returns_false(InvoiceStatus status)
    {
        var invoice = InStatus(status);
        var before = (invoice.Status, invoice.SentAtUtc, invoice.CancelledAtUtc, invoice.UpdatedAtUtc);

        var changed = invoice.Send(Later.AddHours(5));

        Assert.False(changed);
        Assert.Equal(before, (invoice.Status, invoice.SentAtUtc, invoice.CancelledAtUtc, invoice.UpdatedAtUtc));
    }

    // ---- cancel ----

    [Fact]
    public void Cancelling_a_Draft_makes_it_Cancelled_with_a_cancelled_time_and_no_sent_time()
    {
        var invoice = Draft();

        var changed = invoice.Cancel(Later);

        Assert.True(changed);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(Later, invoice.CancelledAtUtc);
        Assert.Null(invoice.SentAtUtc);
        Assert.Equal(Later, invoice.UpdatedAtUtc);
        Assert.False(invoice.IsEditable);
    }

    [Fact]
    public void Cancelling_a_Sent_invoice_keeps_the_time_it_was_sent()
    {
        var invoice = InStatus(InvoiceStatus.Sent);

        var changed = invoice.Cancel(Later.AddHours(1));

        Assert.True(changed);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(Later, invoice.SentAtUtc);
        Assert.Equal(Later.AddHours(1), invoice.CancelledAtUtc);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void Cancelling_a_Paid_or_Cancelled_invoice_changes_nothing_and_returns_false(InvoiceStatus status)
    {
        var invoice = InStatus(status);
        var before = (invoice.Status, invoice.SentAtUtc, invoice.CancelledAtUtc, invoice.UpdatedAtUtc);

        var changed = invoice.Cancel(Later.AddHours(5));

        Assert.False(changed);
        Assert.Equal(before, (invoice.Status, invoice.SentAtUtc, invoice.CancelledAtUtc, invoice.UpdatedAtUtc));
    }

    // ---- Overdue is derived, never stored ----

    [Fact]
    public void A_Sent_invoice_is_overdue_only_when_its_due_date_is_before_today()
    {
        var invoice = InStatus(InvoiceStatus.Sent);

        Assert.False(invoice.IsOverdue(Due.AddDays(-1)));
        Assert.False(invoice.IsOverdue(Due));
        Assert.True(invoice.IsOverdue(Due.AddDays(1)));
        Assert.True(invoice.IsOverdue(Due.AddYears(1)));
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void An_invoice_that_is_not_Sent_is_never_overdue_however_late_it_is(InvoiceStatus status)
    {
        var invoice = InStatus(status);

        Assert.False(invoice.IsOverdue(Due.AddYears(5)));
    }

    [Fact]
    public void The_list_row_rule_and_the_invoice_rule_are_the_same_function()
    {
        var invoice = InStatus(InvoiceStatus.Sent);

        foreach (var today in new[] { Due.AddDays(-1), Due, Due.AddDays(1) })
        {
            Assert.Equal(invoice.IsOverdue(today), Invoice.IsOverdue(invoice.Status, invoice.DueDate, today));
        }

        Assert.False(Invoice.IsOverdue(InvoiceStatus.Draft, Due, Due.AddYears(1)));
    }

    [Fact]
    public void Freezing_several_lines_with_a_discount_that_leaves_a_remainder_keeps_each_line_aligned_with_its_own_amounts()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, null, Now, 10.00m);
        var cheap = Product.Create("Cheap", "CH-1", ProductUnit.Item, 10.00m, 15m, Now);
        var dear = Product.Create("Dear", "DR-1", ProductUnit.Item, 33.33m, 0m, Now);
        var free = Product.Create("Free", "FR-1", ProductUnit.Item, 0.00m, 15m, Now);
        invoice.AddLine(cheap, 1m, Now);
        invoice.AddLine(dear, 3m, Now);
        invoice.AddLine(free, 2m, Now);
        var live = InvoiceMoney.Calculate(
            invoice.Lines.Select(line => new AmountLine(line.Quantity, line.UnitPrice, line.VatPercent)).ToList(),
            10.00m);

        invoice.Send(Later);

        var frozen = invoice.Amounts;
        Assert.Equal(live.Lines, frozen.Lines);
        Assert.Equal(10.00m, frozen.Lines.Sum(line => line.DiscountShare));
        Assert.Equal(live.GrandTotal, frozen.GrandTotal);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(live.Lines[i].Net, invoice.Lines[i].FrozenNet);
            Assert.Equal(live.Lines[i].DiscountShare, invoice.Lines[i].FrozenDiscountShare);
            Assert.Equal(live.Lines[i].Vat, invoice.Lines[i].FrozenVat);
        }

        Assert.Equal(0.00m, frozen.Lines[2].DiscountShare);
    }

    [Fact]
    public void When_only_some_lines_lack_frozen_values_the_amounts_are_calculated_instead_of_failing()
    {
        var invoice = Draft();
        invoice.AddLine(Product.Create("Second", "SC-1", ProductUnit.Item, 50.00m, 0m, Now), 1m, Now);
        invoice.Send(Later);
        Force(invoice.Lines[1], nameof(InvoiceLine.FrozenNet), null);

        var amounts = invoice.Amounts;

        Assert.Equal(280.00m, amounts.GrandTotal);
        Assert.Equal(2, amounts.Lines.Count);
    }

    [Fact]
    public void A_legacy_Draft_with_a_discount_above_its_net_is_frozen_with_the_discount_that_was_applied_so_the_numbers_agree()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, null, Now, 500.00m);
        invoice.AddLine(Consulting(), 2m, Now);

        invoice.Send(Later);

        var amounts = invoice.Amounts;
        Assert.Equal(200.00m, invoice.Discount);
        Assert.Equal(200.00m, amounts.Discount);
        Assert.Equal(amounts.TotalNet - amounts.Discount, amounts.NetAfterDiscount);
        Assert.Equal(0.00m, amounts.GrandTotal);
    }

    [Fact]
    public void Reading_never_throws_even_for_a_discount_that_is_negative()
    {
        var invoice = Draft();
        Force(invoice, nameof(Invoice.Discount), -5.00m);

        var amounts = invoice.Amounts;

        Assert.Equal(0.00m, amounts.Discount);
        Assert.Equal(230.00m, amounts.GrandTotal);
    }

    // ---- frozen amounts ----

    [Fact]
    public void Sending_freezes_the_amounts_and_a_non_Draft_invoice_reads_the_frozen_numbers()
    {
        var invoice = Draft(20.00m);
        var live = invoice.Amounts;

        invoice.Send(Later);

        Assert.Equal(live.TotalNet, invoice.FrozenTotalNet);
        Assert.Equal(live.NetAfterDiscount, invoice.FrozenNetAfterDiscount);
        Assert.Equal(live.TotalVat, invoice.FrozenTotalVat);
        Assert.Equal(live.GrandTotal, invoice.FrozenGrandTotal);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(live.Lines[0].Net, line.FrozenNet);
        Assert.Equal(live.Lines[0].DiscountShare, line.FrozenDiscountShare);
        Assert.Equal(live.Lines[0].Vat, line.FrozenVat);
        Assert.Equal(207.00m, invoice.Amounts.GrandTotal);
    }

    [Fact]
    public void The_amounts_of_a_Sent_invoice_do_not_move_when_a_line_value_changes_afterwards()
    {
        var invoice = Draft();
        invoice.Send(Later);

        // Whatever happens to the stored line values later (a product change would not reach them; a rule change or a bad write might).
        Force(invoice.Lines.Single(), nameof(InvoiceLine.UnitPrice), 999.99m);
        Force(invoice.Lines.Single(), nameof(InvoiceLine.Quantity), 50m);

        Assert.Equal(200.00m, invoice.Amounts.TotalNet);
        Assert.Equal(30.00m, invoice.Amounts.TotalVat);
        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
        Assert.Equal(200.00m, invoice.Amounts.Lines.Single().Net);
    }

    [Fact]
    public void A_Draft_still_calculates_live_from_its_lines()
    {
        var invoice = Draft();

        Force(invoice.Lines.Single(), nameof(InvoiceLine.UnitPrice), 150.00m);

        Assert.Equal(300.00m, invoice.Amounts.TotalNet);
        Assert.Null(invoice.FrozenTotalNet);
    }

    [Fact]
    public void Cancelling_a_Draft_freezes_its_amounts_too()
    {
        var invoice = Draft();

        invoice.Cancel(Later);
        Force(invoice.Lines.Single(), nameof(InvoiceLine.UnitPrice), 999.99m);

        Assert.Equal(230.00m, invoice.FrozenGrandTotal);
        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
    }

    [Fact]
    public void Cancelling_a_Sent_invoice_keeps_the_amounts_frozen_when_it_was_sent()
    {
        var invoice = Draft();
        invoice.Send(Later);
        Force(invoice.Lines.Single(), nameof(InvoiceLine.Quantity), 9m);

        invoice.Cancel(Later.AddHours(1));

        Assert.Equal(230.00m, invoice.FrozenGrandTotal);
        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
    }

    [Fact]
    public void A_non_Draft_invoice_whose_frozen_values_are_missing_still_reads_by_calculating()
    {
        var invoice = Draft();
        Force(invoice, nameof(Invoice.Status), InvoiceStatus.Sent);

        var amounts = invoice.Amounts;

        Assert.Equal(230.00m, amounts.GrandTotal);
    }

    // ---- the edit guard ----

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void Every_edit_of_an_invoice_that_is_not_a_Draft_is_refused_by_the_aggregate(InvoiceStatus status)
    {
        var invoice = InStatus(status);
        var lineId = invoice.Lines.Single().Id;

        Assert.Throws<InvalidOperationException>(() => invoice.UpdateDetails(invoice.CustomerId, Due, "x", 0m, Later));
        Assert.Throws<InvalidOperationException>(() => invoice.AddLine(Consulting(), 1m, Later));
        Assert.Throws<InvalidOperationException>(() => invoice.RemoveLine(lineId, Later));
        Assert.Throws<InvalidOperationException>(() => invoice.SetLineQuantity(lineId, 5m, Later));
        Assert.Equal(2m, invoice.Lines.Single().Quantity);
        Assert.Single(invoice.Lines);
    }

    [Fact]
    public void A_Draft_is_edited_exactly_as_before()
    {
        var invoice = Draft();
        var lineId = invoice.Lines.Single().Id;

        Assert.True(invoice.UpdateDetails(invoice.CustomerId, Due, "changed", 0m, Later));
        Assert.True(invoice.SetLineQuantity(lineId, 3m, Later));
        invoice.AddLine(Consulting(), 1m, Later);
        Assert.True(invoice.RemoveLine(lineId, Later));

        Assert.Single(invoice.Lines);
    }
}
