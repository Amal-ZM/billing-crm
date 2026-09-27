using System.Reflection;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>Sending, cancelling and the Overdue view of an invoice through the service (spec 007), with fakes.</summary>
public class InvoiceServiceStatusTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly FakeCurrentUser _user = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Product _consulting;

    public InvoiceServiceStatusTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Start.UtcDateTime);
        _products.Seed(_consulting);
    }

    /// <summary>Puts a stored invoice (ABC, consulting x 2, 230.00) in the fake store in the given status with the given due date.</summary>
    private Invoice Seeded(InvoiceStatus status, string dueDate = "2030-07-01", decimal discount = 0m)
    {
        var invoice = Invoice.Create(_abc.Id, DateOnly.Parse(dueDate), "Net 30", Start.UtcDateTime, discount);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        switch (status)
        {
            case InvoiceStatus.Sent:
                invoice.Send(Start.UtcDateTime);
                break;
            case InvoiceStatus.Paid:
                invoice.Send(Start.UtcDateTime);
                Force(invoice, nameof(Invoice.Status), InvoiceStatus.Paid);
                break;
            case InvoiceStatus.Cancelled:
                invoice.Cancel(Start.UtcDateTime);
                break;
        }

        _invoices.Seed(invoice);
        return invoice;
    }

    private static void Force(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    private static InvoiceInput Edit(Invoice invoice, decimal quantity = 5m) =>
        new(invoice.CustomerId.ToString(), invoice.DueDate.ToString("yyyy-MM-dd"), invoice.Notes, [new InvoiceLineInput(invoice.Lines[0].Id.ToString(), null, quantity)]);

    private static Error ErrorOf(Result<InvoiceDto> result)
    {
        Assert.True(result.IsFailure);
        return result.Error!;
    }

    // ---- send ----

    [Fact]
    public async Task Sending_a_Draft_makes_it_Sent_with_the_time_and_leaves_the_number_and_amounts_alone()
    {
        var draft = Seeded(InvoiceStatus.Draft);

        var result = await _sut.SendAsync(draft.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("Sent", invoice.Status);
        Assert.Equal(Start.UtcDateTime, invoice.SentAtUtc);
        Assert.Null(invoice.CancelledAtUtc);
        Assert.False(invoice.IsOverdue);
        Assert.False(invoice.IsEditable);
        Assert.Equal(["Cancelled"], invoice.AllowedTransitions);
        Assert.Equal(230.00m, invoice.Totals.GrandTotal);
        Assert.Equal(200.00m, invoice.Lines[0].Net);
        Assert.Equal(1, _invoices.SaveChangesCalls);
        Assert.Equal(InvoiceStatus.Sent, draft.Status);
    }

    [Fact]
    public async Task A_Sent_invoice_keeps_its_amounts_after_the_product_is_repriced_and_deactivated()
    {
        var draft = Seeded(InvoiceStatus.Draft);
        await _sut.SendAsync(draft.Id, CancellationToken.None);

        _consulting.Update("Consulting day", "CNS-002", ProductUnit.Service, 999.00m, 0m, Start.UtcDateTime.AddDays(1));
        _consulting.SetActive(false, Start.UtcDateTime.AddDays(1));
        var opened = (await _sut.GetAsync(draft.Id, CancellationToken.None)).Value;

        Assert.Equal(230.00m, opened.Totals.GrandTotal);
        Assert.Equal(30.00m, opened.Lines[0].Vat);
        Assert.Equal(100.00m, opened.Lines[0].UnitPrice);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent, "Sent")]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public async Task Sending_an_invoice_that_is_not_a_Draft_is_an_illegal_change_naming_the_current_status(InvoiceStatus status, string shown)
    {
        var invoice = Seeded(status);

        var error = ErrorOf(await _sut.SendAsync(invoice.Id, CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_status_change_not_allowed", error.Code);
        Assert.Equal("Illegal status change", error.Title);
        Assert.Equal($"An invoice that is {shown} cannot be sent.", error.Detail);
        Assert.Equal(0, _invoices.SaveChangesCalls);
        Assert.Equal(status, invoice.Status);
    }

    [Fact]
    public async Task Sending_a_Sent_invoice_that_is_past_its_due_date_names_Overdue()
    {
        var invoice = Seeded(InvoiceStatus.Sent, dueDate: "2030-05-01");

        var error = ErrorOf(await _sut.SendAsync(invoice.Id, CancellationToken.None));

        Assert.Equal("An invoice that is Overdue cannot be sent.", error.Detail);
    }

    [Fact]
    public async Task A_Draft_whose_due_date_is_in_the_past_cannot_be_sent_and_stays_a_Draft()
    {
        var draft = Seeded(InvoiceStatus.Draft, dueDate: "2030-05-31");

        var error = ErrorOf(await _sut.SendAsync(draft.Id, CancellationToken.None));

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal(["Due date is in the past. Change the due date before sending."], error.Errors!["dueDate"]);
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Null(draft.SentAtUtc);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task A_Draft_due_today_can_be_sent()
    {
        var draft = Seeded(InvoiceStatus.Draft, dueDate: "2030-06-01");

        var result = await _sut.SendAsync(draft.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsOverdue);
    }

    [Fact]
    public async Task Today_is_the_Saudi_date_so_a_Draft_due_on_the_UTC_day_is_refused_once_it_is_past_midnight_in_Riyadh()
    {
        var first = Seeded(InvoiceStatus.Draft, dueDate: "2030-06-01");
        var second = Seeded(InvoiceStatus.Draft, dueDate: "2030-06-01");

        // 20:59 UTC on 1 June is 23:59 in Riyadh: still the 1st, so due today is fine.
        _time.Advance(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(59));
        Assert.True((await _sut.SendAsync(first.Id, CancellationToken.None)).IsSuccess);

        // One minute later it is 00:00 on 2 June in Riyadh: the same due date is now in the past.
        _time.Advance(TimeSpan.FromMinutes(1));
        var error = ErrorOf(await _sut.SendAsync(second.Id, CancellationToken.None));
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    [Fact]
    public async Task Sending_an_unknown_invoice_is_not_found()
    {
        var error = ErrorOf(await _sut.SendAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorKind.NotFound, error.Kind);
        Assert.Equal("Invoice not found", error.Title);
    }

    [Fact]
    public async Task A_send_that_lost_a_race_is_a_conflict_and_not_an_exception()
    {
        var draft = Seeded(InvoiceStatus.Draft);
        _invoices.SaveLosesRace = true;

        var error = ErrorOf(await _sut.SendAsync(draft.Id, CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_changed", error.Code);
    }

    // ---- a locked invoice cannot be edited ----

    [Theory]
    [InlineData(InvoiceStatus.Sent, "Sent")]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public async Task Editing_an_invoice_that_is_not_a_Draft_is_a_conflict_and_the_invoice_is_unchanged(InvoiceStatus status, string shown)
    {
        var invoice = Seeded(status, discount: 20.00m);
        var before = (invoice.Lines[0].Quantity, invoice.Discount, invoice.Notes, invoice.DueDate, invoice.UpdatedAtUtc, invoice.Lines.Count);

        var error = ErrorOf(await _sut.UpdateAsync(invoice.Id, Edit(invoice, 5m), CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_not_draft", error.Code);
        Assert.Equal("Invoice is locked", error.Title);
        Assert.Equal($"Only a Draft invoice can be edited. This invoice is {shown}.", error.Detail);
        Assert.Equal(before, (invoice.Lines[0].Quantity, invoice.Discount, invoice.Notes, invoice.DueDate, invoice.UpdatedAtUtc, invoice.Lines.Count));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task Every_kind_of_edit_of_a_Sent_invoice_is_refused_the_same_way()
    {
        var invoice = Seeded(InvoiceStatus.Sent, discount: 10.00m);
        var lineId = invoice.Lines[0].Id.ToString();
        var bodies = new[]
        {
            new InvoiceInput(invoice.CustomerId.ToString(), "2030-07-01", "Net 30", [new InvoiceLineInput(lineId, null, 9m)], 10.00m),                    // quantity
            new InvoiceInput(invoice.CustomerId.ToString(), "2030-07-01", "Net 30", [new InvoiceLineInput(lineId, null, 2m), new InvoiceLineInput(null, _consulting.Id.ToString(), 1m)], 10.00m), // add a line
            new InvoiceInput(invoice.CustomerId.ToString(), "2030-07-01", "Net 30", [new InvoiceLineInput(null, _consulting.Id.ToString(), 1m)], 10.00m),  // remove the line
            new InvoiceInput(invoice.CustomerId.ToString(), "2030-07-01", "Net 30", [new InvoiceLineInput(lineId, null, 2m)], 50.00m),                     // discount
            new InvoiceInput(Guid.NewGuid().ToString(), "2030-07-01", "Net 30", [new InvoiceLineInput(lineId, null, 2m)], 10.00m),                        // customer
            new InvoiceInput(invoice.CustomerId.ToString(), "2031-01-01", "Net 30", [new InvoiceLineInput(lineId, null, 2m)], 10.00m),                    // due date
            new InvoiceInput(invoice.CustomerId.ToString(), "2030-07-01", "Changed", [new InvoiceLineInput(lineId, null, 2m)], 10.00m),                   // notes
            new InvoiceInput(null, null, null, null),                                                                                                       // an invalid body
        };

        foreach (var body in bodies)
        {
            var error = ErrorOf(await _sut.UpdateAsync(invoice.Id, body, CancellationToken.None));
            Assert.Equal("invoice_not_draft", error.Code);
        }

        Assert.Equal(0, _invoices.SaveChangesCalls);
        Assert.Equal(2m, invoice.Lines[0].Quantity);
        Assert.Equal(10.00m, invoice.Discount);
    }

    [Fact]
    public async Task An_unknown_invoice_is_not_found_before_anything_about_its_status_is_judged()
    {
        var error = ErrorOf(await _sut.UpdateAsync(Guid.NewGuid(), new InvoiceInput(null, null, null, null), CancellationToken.None));

        Assert.Equal(ErrorKind.NotFound, error.Kind);
    }

    [Fact]
    public async Task A_Draft_is_still_edited_as_before()
    {
        var draft = Seeded(InvoiceStatus.Draft);

        var result = await _sut.UpdateAsync(draft.Id, Edit(draft, 3m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3m, result.Value.Lines[0].Quantity);
        Assert.Equal("Draft", result.Value.Status);
        Assert.True(result.Value.IsEditable);
        Assert.Equal(["Sent", "Cancelled"], result.Value.AllowedTransitions);
    }

    // ---- cancel ----

    [Fact]
    public async Task Cancelling_a_Draft_makes_it_Cancelled_with_the_time_and_no_sent_time_and_keeps_the_number_lines_and_amounts()
    {
        var draft = Seeded(InvoiceStatus.Draft, discount: 20.00m);

        var result = await _sut.CancelAsync(draft.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("Cancelled", invoice.Status);
        Assert.Equal(Start.UtcDateTime, invoice.CancelledAtUtc);
        Assert.Null(invoice.SentAtUtc);
        Assert.False(invoice.IsOverdue);
        Assert.False(invoice.IsEditable);
        Assert.Empty(invoice.AllowedTransitions);
        Assert.Equal(207.00m, invoice.Totals.GrandTotal);
        Assert.Single(invoice.Lines);
        Assert.Equal(1, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task A_cancelled_Draft_keeps_its_amounts_after_the_product_is_repriced()
    {
        var draft = Seeded(InvoiceStatus.Draft);
        await _sut.CancelAsync(draft.Id, CancellationToken.None);

        _consulting.Update("Consulting day", "CNS-002", ProductUnit.Service, 999.00m, 0m, Start.UtcDateTime.AddDays(1));
        var opened = (await _sut.GetAsync(draft.Id, CancellationToken.None)).Value;

        Assert.Equal(230.00m, opened.Totals.GrandTotal);
    }

    [Fact]
    public async Task Cancelling_a_Sent_invoice_keeps_the_time_it_was_sent_and_the_amounts_frozen_then()
    {
        var sent = Seeded(InvoiceStatus.Sent);
        _time.Advance(TimeSpan.FromHours(4));

        var result = await _sut.CancelAsync(sent.Id, CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal("Cancelled", invoice.Status);
        Assert.Equal(Start.UtcDateTime, invoice.SentAtUtc);
        Assert.Equal(Start.UtcDateTime.AddHours(4), invoice.CancelledAtUtc);
        Assert.Equal(230.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task An_Overdue_invoice_can_be_cancelled_and_is_then_no_longer_shown_as_overdue()
    {
        var late = Seeded(InvoiceStatus.Sent, dueDate: "2030-05-01");
        Assert.True((await _sut.GetAsync(late.Id, CancellationToken.None)).Value.IsOverdue);

        var result = await _sut.CancelAsync(late.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value.Status);
        Assert.False(result.Value.IsOverdue);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public async Task Cancelling_a_Paid_or_Cancelled_invoice_is_an_illegal_change_naming_the_current_status(InvoiceStatus status, string shown)
    {
        var invoice = Seeded(status);

        var error = ErrorOf(await _sut.CancelAsync(invoice.Id, CancellationToken.None));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("invoice_status_change_not_allowed", error.Code);
        Assert.Equal($"An invoice that is {shown} cannot be cancelled.", error.Detail);
        Assert.Equal(0, _invoices.SaveChangesCalls);
        Assert.Equal(status, invoice.Status);
    }

    [Fact]
    public async Task Cancelling_an_unknown_invoice_is_not_found()
    {
        var error = ErrorOf(await _sut.CancelAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorKind.NotFound, error.Kind);
    }

    [Fact]
    public async Task A_cancel_that_lost_a_race_is_a_conflict_and_not_an_exception()
    {
        var draft = Seeded(InvoiceStatus.Draft);
        _invoices.SaveLosesRace = true;

        var error = ErrorOf(await _sut.CancelAsync(draft.Id, CancellationToken.None));

        Assert.Equal("invoice_changed", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
    }

    [Fact]
    public async Task A_Cancelled_invoice_is_final_it_cannot_be_sent_or_edited_or_cancelled_again()
    {
        var draft = Seeded(InvoiceStatus.Draft);
        await _sut.CancelAsync(draft.Id, CancellationToken.None);
        var calls = _invoices.SaveChangesCalls;

        var send = ErrorOf(await _sut.SendAsync(draft.Id, CancellationToken.None));
        var edit = ErrorOf(await _sut.UpdateAsync(draft.Id, Edit(draft, 4m), CancellationToken.None));
        var again = ErrorOf(await _sut.CancelAsync(draft.Id, CancellationToken.None));

        Assert.Equal("An invoice that is Cancelled cannot be sent.", send.Detail);
        Assert.Equal("invoice_not_draft", edit.Code);
        Assert.Equal("An invoice that is Cancelled cannot be cancelled.", again.Detail);
        Assert.Equal(calls, _invoices.SaveChangesCalls);
        Assert.Equal(InvoiceStatus.Cancelled, draft.Status);
    }

    // ---- Overdue is derived (US3) ----

    [Theory]
    [InlineData(InvoiceStatus.Sent, "2030-06-01", false)]    // due today
    [InlineData(InvoiceStatus.Sent, "2030-06-02", false)]    // due tomorrow
    [InlineData(InvoiceStatus.Sent, "2030-05-31", true)]     // due yesterday
    [InlineData(InvoiceStatus.Sent, "2020-01-01", true)]
    [InlineData(InvoiceStatus.Draft, "2030-05-31", false)]
    [InlineData(InvoiceStatus.Paid, "2030-05-31", false)]
    [InlineData(InvoiceStatus.Cancelled, "2030-05-31", false)]
    public async Task An_invoice_is_overdue_only_when_it_is_Sent_and_due_before_today_in_get_and_in_the_list(InvoiceStatus status, string due, bool expected)
    {
        var invoice = Seeded(status, dueDate: due);

        var opened = (await _sut.GetAsync(invoice.Id, CancellationToken.None)).Value;
        var listed = (await _sut.SearchAsync(new InvoiceQuery(null, null), CancellationToken.None)).Value.Items.Single();

        Assert.Equal(expected, opened.IsOverdue);
        Assert.Equal(expected, listed.IsOverdue);
        Assert.Equal(status.ToString(), opened.Status);
        Assert.Equal(status.ToString(), listed.Status);
    }

    [Fact]
    public async Task A_Sent_invoice_becomes_overdue_at_midnight_in_Riyadh_with_nobody_saving_anything()
    {
        var invoice = Seeded(InvoiceStatus.Sent, dueDate: "2030-06-01");

        // 20:59 UTC on 1 June is 23:59 in Riyadh: still the due date, not late.
        _time.Advance(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(59));
        Assert.False((await _sut.GetAsync(invoice.Id, CancellationToken.None)).Value.IsOverdue);

        // 21:00 UTC is 00:00 on 2 June in Riyadh: the due date is now yesterday.
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _sut.GetAsync(invoice.Id, CancellationToken.None)).Value.IsOverdue);
        Assert.Equal(0, _invoices.SaveChangesCalls);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public async Task An_Overdue_invoice_is_still_a_Sent_invoice_locked_cancellable_and_not_sendable()
    {
        var invoice = Seeded(InvoiceStatus.Sent, dueDate: "2030-05-01");

        var opened = (await _sut.GetAsync(invoice.Id, CancellationToken.None)).Value;

        Assert.True(opened.IsOverdue);
        Assert.Equal("Sent", opened.Status);
        Assert.False(opened.IsEditable);
        Assert.Equal(["Cancelled"], opened.AllowedTransitions);
        Assert.Equal("An invoice that is Overdue cannot be sent.", ErrorOf(await _sut.SendAsync(invoice.Id, CancellationToken.None)).Detail);
        Assert.Equal("Only a Draft invoice can be edited. This invoice is Overdue.", ErrorOf(await _sut.UpdateAsync(invoice.Id, Edit(invoice), CancellationToken.None)).Detail);
    }

    [Fact]
    public async Task Cancelling_does_not_leave_a_gap_in_the_invoice_numbers()
    {
        var input = new InvoiceInput(_abc.Id.ToString(), "2030-07-01", null, [new InvoiceLineInput(null, _consulting.Id.ToString(), 1m)]);
        var first = (await _sut.CreateAsync(input, CancellationToken.None)).Value;
        await _sut.CancelAsync(first.Id, CancellationToken.None);

        var second = (await _sut.CreateAsync(input, CancellationToken.None)).Value;

        Assert.Equal("INV-000001", first.Number);
        Assert.Equal("INV-000002", second.Number);
        Assert.Equal("INV-000001", (await _sut.GetAsync(first.Id, CancellationToken.None)).Value.Number);
    }
}
