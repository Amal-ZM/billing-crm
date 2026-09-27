using BillingCrm.Application.Auth;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>
/// History recording through the service (spec 010): exactly one entry per successful change, written with the change and never without it,
/// none for anything refused, unchanged or lost, always the signed-in user and the clock's time, and values as they were at the time.
/// </summary>
public class InvoiceServiceHistoryTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor AgentActor = new("agent1@crm.local", StaffRole.Agent);
    private static readonly Actor AdminActor = new("admin@crm.local", StaffRole.Admin);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly FakeCurrentUser _user = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Customer _other;
    private readonly Product _consulting;
    private readonly Product _web;

    public InvoiceServiceHistoryTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime);
        _other = Customer.Create("Other Co", CustomerType.Company, "accounts@other.example", "0500000001", null, "Jeddah", Start.UtcDateTime);
        _customers.Seed(_abc);
        _customers.Seed(_other);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Start.UtcDateTime);
        _web = Product.Create("Website build", "WEB-9", ProductUnit.Service, 5000.50m, 0m, Start.UtcDateTime);
        _products.Seed(_consulting);
        _products.Seed(_web);
    }

    private static InvoiceLineInput New(Product product, decimal quantity) => new(null, product.Id.ToString(), quantity);

    private static InvoiceLineInput Keep(InvoiceLineDto line, decimal? quantity = null) => new(line.Id.ToString(), null, quantity ?? line.Quantity);

    private InvoiceInput Input(Customer? customer = null, string dueDate = "2030-07-01", string? notes = "Net 30", params InvoiceLineInput[] lines) =>
        new((customer ?? _abc).Id.ToString(), dueDate, notes, lines.Length == 0 ? [New(_consulting, 2m)] : lines);

    private static InvoiceDto Ok(Result<InvoiceDto> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    private async Task<InvoiceDto> CreateDraftAsync() => Ok(await _sut.CreateAsync(Input(), CancellationToken.None));

    /// <summary>A stored Sent invoice (consulting x 2, grand total 230.00), put in the store directly: it has no history, like an older invoice.</summary>
    private Invoice SeededSent(string dueDate = "2030-07-01")
    {
        var invoice = Invoice.Create(_abc.Id, DateOnly.Parse(dueDate), "Net 30", Start.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        Assert.True(invoice.Send(Start.UtcDateTime));
        _invoices.Seed(invoice);
        return invoice;
    }

    private Invoice SeededDraft()
    {
        var invoice = Invoice.Create(_abc.Id, new DateOnly(2030, 7, 1), "Net 30", Start.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        _invoices.Seed(invoice);
        return invoice;
    }

    private static PaymentInput Pay(decimal amount, string method = "Cash", string? note = null, string date = "2030-06-01") => new(date, amount, method, note);

    private InvoiceHistoryEntry OnlyEntry(Guid invoiceId) => Assert.Single(_invoices.HistoryOf(invoiceId));

    private void AssertNothingPending() => Assert.Equal(0, _invoices.PendingHistoryCount);

    // ---- one entry per successful change ----

    [Fact]
    public async Task Creating_an_invoice_writes_one_Created_entry_with_the_invoice_in_the_same_add()
    {
        var created = await CreateDraftAsync();

        var entry = OnlyEntry(created.Id);
        Assert.Equal(HistoryKind.Created, entry.Kind);
        Assert.Equal(created.Id, entry.InvoiceId);
        Assert.Equal("agent1@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Agent, entry.ActorRole);
        Assert.Equal(Start.UtcDateTime, entry.OccurredAtUtc);
        Assert.Contains(new HistoryChange("Customer", null, "ABC"), entry.Changes);
        Assert.Contains(new HistoryChange("Line: Consulting hour (CNS-001)", null, "2.00 x SAR 100.00, VAT 15.00%"), entry.Changes);
        Assert.Equal(InvoiceHistoryBuilder.Created(_invoices.All.Single(), "ABC", AgentActor, Start.UtcDateTime).Changes, entry.Changes);
        Assert.Equal(1, _invoices.AddCalls);
        AssertNothingPending();
    }

    [Fact]
    public async Task Editing_a_Draft_writes_one_DraftEdited_entry_with_only_what_changed()
    {
        var created = await CreateDraftAsync();
        _time.Advance(TimeSpan.FromHours(2));

        var edited = Ok(await _sut.UpdateAsync(created.Id, Input(notes: "Pay on receipt", lines: Keep(created.Lines[0], 5m)), CancellationToken.None));

        Assert.Equal(2, _invoices.HistoryOf(created.Id).Count);
        var entry = _invoices.HistoryOf(created.Id)[1];
        Assert.Equal(HistoryKind.DraftEdited, entry.Kind);
        Assert.Equal(Start.AddHours(2).UtcDateTime, entry.OccurredAtUtc);
        Assert.Equal(edited.UpdatedAtUtc, entry.OccurredAtUtc);
        Assert.Equal(
            [
                new HistoryChange("Notes", "Net 30", "Pay on receipt"),
                new HistoryChange("Line quantity: Consulting hour (CNS-001)", "2.00", "5.00"),
            ],
            entry.Changes);
        AssertNothingPending();
    }

    [Fact]
    public async Task An_edit_that_changes_the_customer_records_the_new_customers_name_after_and_the_previous_name_before()
    {
        var created = await CreateDraftAsync();

        Ok(await _sut.UpdateAsync(created.Id, Input(_other, lines: Keep(created.Lines[0])), CancellationToken.None));

        Assert.Equal([new HistoryChange("Customer", "ABC", "Other Co")], _invoices.HistoryOf(created.Id)[1].Changes);
    }

    [Fact]
    public async Task An_edit_that_adds_and_removes_lines_records_them()
    {
        var created = await CreateDraftAsync();

        Ok(await _sut.UpdateAsync(created.Id, Input(lines: New(_web, 1m)), CancellationToken.None));

        Assert.Equal(
            [
                new HistoryChange("Line added: Website build (WEB-9)", null, "1.00 x SAR 5000.50, VAT 0.00%"),
                new HistoryChange("Line removed: Consulting hour (CNS-001)", "2.00 x SAR 100.00, VAT 15.00%", null),
            ],
            _invoices.HistoryOf(created.Id)[1].Changes);
    }

    [Fact]
    public async Task Sending_writes_one_Sent_entry()
    {
        var invoice = SeededDraft();
        _time.Advance(TimeSpan.FromMinutes(10));

        Ok(await _sut.SendAsync(invoice.Id, CancellationToken.None));

        var entry = OnlyEntry(invoice.Id);
        Assert.Equal(HistoryKind.Sent, entry.Kind);
        Assert.Equal([new HistoryChange("Status", "Draft", "Sent")], entry.Changes);
        Assert.Equal("agent1@crm.local", entry.ActorEmail);
        Assert.Equal(Start.AddMinutes(10).UtcDateTime, entry.OccurredAtUtc);
        Assert.Equal(invoice.SentAtUtc, entry.OccurredAtUtc);
        AssertNothingPending();
    }

    [Fact]
    public async Task A_part_payment_and_then_the_settling_payment_write_one_Payment_entry_each()
    {
        var invoice = SeededSent();

        Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(100m, "Cash", "first"), CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(5));
        Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(130m, "Bank"), CancellationToken.None));

        var entries = _invoices.HistoryOf(invoice.Id);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(HistoryKind.Payment, entry.Kind));

        Assert.Contains(new HistoryChange("Payment amount (SAR)", null, "100.00"), entries[0].Changes);
        Assert.Contains(new HistoryChange("Payment note", null, "first"), entries[0].Changes);
        Assert.Contains(new HistoryChange("Amount paid (SAR)", "0.00", "100.00"), entries[0].Changes);
        Assert.Contains(new HistoryChange("Amount remaining (SAR)", "230.00", "130.00"), entries[0].Changes);
        Assert.DoesNotContain(entries[0].Changes, change => change.Field == "Status");

        Assert.Contains(new HistoryChange("Payment method", null, "Bank"), entries[1].Changes);
        Assert.Contains(new HistoryChange("Amount paid (SAR)", "100.00", "230.00"), entries[1].Changes);
        Assert.Contains(new HistoryChange("Amount remaining (SAR)", "130.00", "0.00"), entries[1].Changes);
        Assert.Contains(new HistoryChange("Status", "Sent", "Paid"), entries[1].Changes);
        Assert.Equal(Start.AddMinutes(5).UtcDateTime, entries[1].OccurredAtUtc);
        AssertNothingPending();
    }

    [Fact]
    public async Task Cancelling_a_Draft_and_cancelling_a_Sent_invoice_each_write_one_Cancelled_entry()
    {
        var draft = SeededDraft();
        var sent = SeededSent();
        _user.Current = AdminActor;

        Ok(await _sut.CancelAsync(draft.Id, CancellationToken.None));
        Ok(await _sut.CancelAsync(sent.Id, CancellationToken.None));

        var fromDraft = OnlyEntry(draft.Id);
        var fromSent = OnlyEntry(sent.Id);
        Assert.Equal(HistoryKind.Cancelled, fromDraft.Kind);
        Assert.Equal([new HistoryChange("Status", "Draft", "Cancelled")], fromDraft.Changes);
        Assert.Equal([new HistoryChange("Status", "Sent", "Cancelled")], fromSent.Changes);
        Assert.Equal(StaffRole.Admin, fromSent.ActorRole);
        Assert.Equal("admin@crm.local", fromSent.ActorEmail);
    }

    [Fact]
    public async Task Cancelling_a_late_Sent_invoice_records_Sent_never_Overdue()
    {
        var invoice = SeededSent(dueDate: "2030-06-05");
        _time.Advance(TimeSpan.FromDays(10));

        Ok(await _sut.CancelAsync(invoice.Id, CancellationToken.None));

        Assert.Equal([new HistoryChange("Status", "Sent", "Cancelled")], OnlyEntry(invoice.Id).Changes);
    }

    // ---- the user is the signed-in user ----

    [Fact]
    public async Task The_entry_carries_whoever_is_signed_in_at_that_moment()
    {
        var created = await CreateDraftAsync();
        _user.Current = AdminActor;

        Ok(await _sut.SendAsync(created.Id, CancellationToken.None));

        var entries = _invoices.HistoryOf(created.Id);
        Assert.Equal(("agent1@crm.local", StaffRole.Agent), (entries[0].ActorEmail, entries[0].ActorRole));
        Assert.Equal(("admin@crm.local", StaffRole.Admin), (entries[1].ActorEmail, entries[1].ActorRole));
    }

    [Fact]
    public async Task With_no_usable_user_a_create_throws_and_writes_neither_the_invoice_nor_an_entry()
    {
        _user.Missing = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(Input(), CancellationToken.None));

        Assert.Equal(0, _invoices.AddCalls);
        Assert.Empty(_invoices.All);
        AssertNothingPending();
    }

    [Fact]
    public async Task With_no_usable_user_an_edit_send_payment_and_cancel_throw_and_change_and_record_nothing()
    {
        var draft = SeededDraft();
        var sent = SeededSent();
        _user.Missing = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateAsync(draft.Id, Input(notes: "changed", lines: Keep(InvoiceDto.From(draft, _abc, new DateOnly(2030, 6, 1)).Lines[0])), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SendAsync(draft.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RecordPaymentAsync(sent.Id, Pay(10m), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CancelAsync(sent.Id, CancellationToken.None));

        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Equal("Net 30", draft.Notes);
        Assert.Equal(InvoiceStatus.Sent, sent.Status);
        Assert.Empty(sent.Payments);
        Assert.Equal(0, _invoices.SaveChangesCalls);
        Assert.Empty(_invoices.HistoryOf(draft.Id));
        Assert.Empty(_invoices.HistoryOf(sent.Id));
        AssertNothingPending();
    }

    // ---- no entry for what did not change ----

    [Fact]
    public async Task An_edit_that_changes_nothing_writes_no_entry_and_does_not_save()
    {
        var created = await CreateDraftAsync();
        var saves = _invoices.SaveChangesCalls;

        Ok(await _sut.UpdateAsync(created.Id, Input(lines: Keep(created.Lines[0])), CancellationToken.None));

        Assert.Equal(saves, _invoices.SaveChangesCalls);
        Assert.Equal(HistoryKind.Created, OnlyEntry(created.Id).Kind);
        AssertNothingPending();
    }

    // ---- no entry for what was refused ----

    [Fact]
    public async Task A_create_that_fails_validation_writes_no_entry_and_records_none()
    {
        var result = await _sut.CreateAsync(new InvoiceInput(null, "not a date", null, []), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_invoices.All);
        AssertNothingPending();
    }

    [Fact]
    public async Task An_edit_that_fails_validation_writes_no_entry_and_records_none()
    {
        var created = await CreateDraftAsync();

        var result = await _sut.UpdateAsync(created.Id, Input(dueDate: "2020-01-01", lines: Keep(created.Lines[0], 0m)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(HistoryKind.Created, OnlyEntry(created.Id).Kind);
        AssertNothingPending();
    }

    [Fact]
    public async Task A_locked_edit_an_illegal_send_and_an_illegal_cancel_write_no_entry()
    {
        var sent = SeededSent();

        Assert.True((await _sut.UpdateAsync(sent.Id, Input(notes: "x"), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.SendAsync(sent.Id, CancellationToken.None)).IsFailure);
        Ok(await _sut.CancelAsync(sent.Id, CancellationToken.None));
        var afterCancel = _invoices.HistoryOf(sent.Id).Count;
        Assert.True((await _sut.CancelAsync(sent.Id, CancellationToken.None)).IsFailure);
        Assert.True((await _sut.SendAsync(sent.Id, CancellationToken.None)).IsFailure);

        Assert.Equal(1, afterCancel);
        Assert.Single(_invoices.HistoryOf(sent.Id));
        AssertNothingPending();
    }

    [Fact]
    public async Task A_send_of_a_Draft_whose_due_date_has_passed_writes_no_entry()
    {
        var invoice = Invoice.Create(_abc.Id, new DateOnly(2030, 7, 1), "Net 30", Start.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        _invoices.Seed(invoice);
        _time.Advance(TimeSpan.FromDays(60));

        var result = await _sut.SendAsync(invoice.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_invoices.HistoryOf(invoice.Id));
        AssertNothingPending();
    }

    [Fact]
    public async Task A_cancel_refused_because_the_invoice_has_payments_writes_no_entry()
    {
        var invoice = SeededSent();
        Ok(await _sut.RecordPaymentAsync(invoice.Id, Pay(50m), CancellationToken.None));

        var result = await _sut.CancelAsync(invoice.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(HistoryKind.Payment, OnlyEntry(invoice.Id).Kind);
        AssertNothingPending();
    }

    [Fact]
    public async Task A_payment_refused_on_a_Draft_for_overpaying_for_a_bad_body_or_an_unknown_invoice_writes_no_entry()
    {
        var draft = SeededDraft();
        var sent = SeededSent();

        Assert.True((await _sut.RecordPaymentAsync(draft.Id, Pay(10m), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.RecordPaymentAsync(sent.Id, Pay(230.01m), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.RecordPaymentAsync(sent.Id, Pay(0m), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.RecordPaymentAsync(sent.Id, Pay(10m, "Bitcoin"), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.RecordPaymentAsync(Guid.NewGuid(), Pay(10m), CancellationToken.None)).IsFailure);

        Assert.Empty(_invoices.HistoryOf(draft.Id));
        Assert.Empty(_invoices.HistoryOf(sent.Id));
        Assert.Equal(0, _invoices.SaveChangesCalls);
        AssertNothingPending();
    }

    [Fact]
    public async Task An_unknown_invoice_id_writes_no_entry_for_any_change()
    {
        var missing = Guid.NewGuid();

        Assert.True((await _sut.UpdateAsync(missing, Input(), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.SendAsync(missing, CancellationToken.None)).IsFailure);
        Assert.True((await _sut.CancelAsync(missing, CancellationToken.None)).IsFailure);

        Assert.Empty(_invoices.HistoryOf(missing));
        AssertNothingPending();
    }

    // ---- a lost race writes neither the change nor the entry ----

    [Fact]
    public async Task A_send_that_loses_a_race_leaves_no_entry_and_nothing_pending()
    {
        var invoice = SeededDraft();
        _invoices.SaveLosesRace = true;

        var result = await _sut.SendAsync(invoice.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_invoices.HistoryOf(invoice.Id));
        AssertNothingPending();
    }

    [Fact]
    public async Task An_edit_a_payment_and_a_cancel_that_lose_a_race_leave_no_entry_and_nothing_pending()
    {
        var draft = SeededDraft();
        var sent = SeededSent();
        _invoices.SaveLosesRace = true;

        Assert.True((await _sut.UpdateAsync(draft.Id, Input(notes: "changed"), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.RecordPaymentAsync(sent.Id, Pay(10m), CancellationToken.None)).IsFailure);
        Assert.True((await _sut.CancelAsync(sent.Id, CancellationToken.None)).IsFailure);

        Assert.Empty(_invoices.HistoryOf(draft.Id));
        Assert.Empty(_invoices.HistoryOf(sent.Id));
        AssertNothingPending();
    }

    // ---- values as at the time ----

    [Fact]
    public async Task An_entry_does_not_change_when_the_customer_or_the_product_is_changed_afterwards()
    {
        var created = await CreateDraftAsync();
        var before = OnlyEntry(created.Id).Changes.ToList();

        _customers.All.Single(customer => customer.Id == _abc.Id)
            .Update("ABC Renamed", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime.AddDays(1));
        _products.All.Single(product => product.Id == _consulting.Id)
            .Update("Consulting hour (new name)", "CNS-001", ProductUnit.Hour, 999m, 5m, Start.UtcDateTime.AddDays(1));

        Assert.Equal(before, OnlyEntry(created.Id).Changes);
        Assert.Contains(new HistoryChange("Customer", null, "ABC"), OnlyEntry(created.Id).Changes);
        Assert.Contains(new HistoryChange("Line: Consulting hour (CNS-001)", null, "2.00 x SAR 100.00, VAT 15.00%"), OnlyEntry(created.Id).Changes);
    }

    [Fact]
    public async Task An_edit_after_the_customer_was_renamed_records_the_name_at_the_time_of_the_edit()
    {
        var created = await CreateDraftAsync();
        _customers.All.Single(customer => customer.Id == _abc.Id)
            .Update("ABC Renamed", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime.AddDays(1));

        Ok(await _sut.UpdateAsync(created.Id, Input(_other, lines: Keep(created.Lines[0])), CancellationToken.None));

        Assert.Equal([new HistoryChange("Customer", "ABC Renamed", "Other Co")], _invoices.HistoryOf(created.Id)[1].Changes);
    }

    // ---- an invoice put in the store directly has no history ----

    [Fact]
    public void An_invoice_put_in_the_store_directly_has_no_entries_like_an_older_invoice()
    {
        var invoice = SeededSent();

        Assert.Empty(_invoices.HistoryOf(invoice.Id));
    }
}
