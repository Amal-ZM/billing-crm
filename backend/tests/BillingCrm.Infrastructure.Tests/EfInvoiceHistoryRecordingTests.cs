using BillingCrm.Application.Auth;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Persistence;
using BillingCrm.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// History recording on real SQL Server with the real service and store (spec 010): every event writes one entry that reads back, the entry and
/// the change are saved together or not at all, and a lost race or a failed save leaves nothing extra behind.
/// </summary>
public class EfInvoiceHistoryRecordingTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Actor Agent = new("agent1@crm.local", StaffRole.Agent);
    private static readonly Actor Admin = new("admin@crm.local", StaffRole.Admin);

    private static DateOnly TodaySaudi() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private InvoiceService NewService(BillingDbContext context, Actor? actor = null) =>
        new(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser { Current = actor ?? Agent });

    private async Task<Customer> AddCustomerAsync(string name = "ABC")
    {
        var customer = Customer.Create(name, CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync()
    {
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private static string Due => TodaySaudi().AddDays(30).ToString("yyyy-MM-dd");

    private static InvoiceInput Input(Customer customer, Product product, string? notes = "Net 30", decimal quantity = 2m) =>
        new(customer.Id.ToString(), Due, notes, [new InvoiceLineInput(null, product.Id.ToString(), quantity)]);

    private static InvoiceDto Ok(Result<InvoiceDto> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    private async Task<InvoiceDto> CreateDraftAsync(string? notes = "Net 30", Actor? actor = null)
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        await using var context = database.CreateContext();
        return Ok(await NewService(context, actor).CreateAsync(Input(customer, product, notes), default));
    }

    private async Task<IReadOnlyList<InvoiceHistoryEntry>> HistoryAsync(Guid invoiceId)
    {
        await using var context = database.CreateContext();
        return await new EfInvoiceStore(context).ListHistoryAsync(invoiceId, default);
    }

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return (await new EfInvoiceStore(context).FindByIdAsync(id, default))!;
    }

    private static PaymentInput Pay(decimal amount, string method = "Cash", string? note = null) =>
        new(TodaySaudi().ToString("yyyy-MM-dd"), amount, method, note);

    // ---- each of the five events ----

    [Fact]
    public async Task Creating_an_invoice_writes_a_Created_entry_that_reads_back_with_the_user_and_the_values_at_the_time()
    {
        var created = await CreateDraftAsync("ملاحظة / note");

        var entry = Assert.Single(await HistoryAsync(created.Id));
        Assert.Equal(HistoryKind.Created, entry.Kind);
        Assert.Equal("agent1@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Agent, entry.ActorRole);
        Assert.Equal(DateTimeKind.Utc, entry.OccurredAtUtc.Kind);
        Assert.True((DateTime.UtcNow - entry.OccurredAtUtc).Duration() < TimeSpan.FromMinutes(1));
        Assert.Contains(new HistoryChange("Customer", null, "ABC"), entry.Changes);
        Assert.Contains(new HistoryChange("Notes", null, "ملاحظة / note"), entry.Changes);
        Assert.Contains(entry.Changes, change => change.Field.StartsWith("Line: Consulting hour", StringComparison.Ordinal) && change.After == "2.00 x SAR 100.00, VAT 15.00%");
    }

    [Fact]
    public async Task Editing_sending_paying_and_cancelling_each_write_one_entry_of_the_right_kind()
    {
        var draft = await CreateDraftAsync();
        await using (var context = database.CreateContext())
        {
            var loaded = (await new EfInvoiceStore(context).FindByIdAsync(draft.Id, default))!;
            var input = new InvoiceInput(loaded.CustomerId.ToString(), Due, "Pay on receipt", [new InvoiceLineInput(loaded.Lines[0].Id.ToString(), null, 3m)]);
            Ok(await NewService(context).UpdateAsync(draft.Id, input, default));
        }

        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).SendAsync(draft.Id, default));
        }

        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context, Admin).RecordPaymentAsync(draft.Id, Pay(100.10m, "Bank", "دفعة"), default));
        }

        var other = await CreateDraftAsync();
        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context, Admin).CancelAsync(other.Id, default));
        }

        var entries = (await HistoryAsync(draft.Id)).Reverse().ToList();
        Assert.Equal([HistoryKind.Created, HistoryKind.DraftEdited, HistoryKind.Sent, HistoryKind.Payment], entries.Select(entry => entry.Kind));
        Assert.Contains(new HistoryChange("Notes", "Net 30", "Pay on receipt"), entries[1].Changes);
        Assert.Contains(entries[1].Changes, change => change.Field.StartsWith("Line quantity:", StringComparison.Ordinal) && change.Before == "2.00" && change.After == "3.00");
        Assert.Equal([new HistoryChange("Status", "Draft", "Sent")], entries[2].Changes);
        Assert.Equal("admin@crm.local", entries[3].ActorEmail);
        Assert.Equal(StaffRole.Admin, entries[3].ActorRole);
        Assert.Contains(new HistoryChange("Payment amount (SAR)", null, "100.10"), entries[3].Changes);
        Assert.Contains(new HistoryChange("Payment note", null, "دفعة"), entries[3].Changes);

        var cancelled = (await HistoryAsync(other.Id)).First();
        Assert.Equal(HistoryKind.Cancelled, cancelled.Kind);
        Assert.Equal([new HistoryChange("Status", "Draft", "Cancelled")], cancelled.Changes);
        Assert.Equal(StaffRole.Admin, cancelled.ActorRole);
    }

    // ---- saved together ----

    [Fact]
    public async Task The_change_and_its_entry_are_both_visible_to_another_context_and_a_settling_payment_is_one_Payment_entry()
    {
        var draft = await CreateDraftAsync();
        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).SendAsync(draft.Id, default));
        }

        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).RecordPaymentAsync(draft.Id, Pay(100m), default));
        }

        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).RecordPaymentAsync(draft.Id, Pay(130m, "Bank"), default));
        }

        Assert.Equal(InvoiceStatus.Paid, (await ReloadAsync(draft.Id)).Status);
        var payments = (await HistoryAsync(draft.Id)).Where(entry => entry.Kind == HistoryKind.Payment).OrderBy(entry => entry.Sequence).ToList();
        Assert.Equal(2, payments.Count);
        Assert.DoesNotContain(payments[0].Changes, change => change.Field == "Status");
        Assert.Contains(new HistoryChange("Status", "Sent", "Paid"), payments[1].Changes);
        Assert.Contains(new HistoryChange("Amount remaining (SAR)", "130.00", "0.00"), payments[1].Changes);
        Assert.Equal(4, (await HistoryAsync(draft.Id)).Count); // Created, Sent and the two payments: no sixth kind for "Paid"
    }

    // ---- a lost race leaves exactly one entry ----

    [Fact]
    public async Task Two_people_sending_the_same_Draft_at_once_leave_exactly_one_Sent_entry_and_the_loser_gets_a_conflict()
    {
        var draft = await CreateDraftAsync();
        await using var winner = database.CreateContext();
        await using var loser = database.CreateContext();

        // The loser has already loaded the Draft (a stale copy) when the winner sends it.
        await new EfInvoiceStore(loser).FindByIdAsync(draft.Id, default);
        Ok(await NewService(winner).SendAsync(draft.Id, default));
        var lost = await NewService(loser, Admin).SendAsync(draft.Id, default);

        Assert.True(lost.IsFailure);
        Assert.Equal(ErrorKind.Conflict, lost.Error!.Kind);
        var entries = await HistoryAsync(draft.Id);
        Assert.Single(entries, entry => entry.Kind == HistoryKind.Sent);
        Assert.Equal("agent1@crm.local", entries.Single(entry => entry.Kind == HistoryKind.Sent).ActorEmail);
        Assert.Equal(2, entries.Count);
        Assert.Equal(InvoiceStatus.Sent, (await ReloadAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task Two_people_paying_from_the_same_balance_leave_one_Payment_entry()
    {
        var draft = await CreateDraftAsync();
        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).SendAsync(draft.Id, default));
        }

        // Both contexts hold the invoice at the same balance before either saves (the service loads and saves in one call, so this is
        // done with the store, recording the entry exactly as the service does): each payment is fine alone, together they would overpay.
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(draft.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(draft.Id, default))!;

        var results = new List<bool>();
        foreach (var (store, copy, amount, method) in new[] { (firstStore, firstCopy, 200m, PaymentMethod.Cash), (secondStore, secondCopy, 200m, PaymentMethod.Bank) })
        {
            var paidBefore = copy.Paid;
            var remainingBefore = copy.Remaining;
            var payment = copy.RecordPayment(TodaySaudi(), amount, method, null, DateTime.UtcNow);
            store.RecordHistory(InvoiceHistoryBuilder.PaymentRecorded(payment, paidBefore, remainingBefore, copy.Paid, copy.Remaining, false, Agent, DateTime.UtcNow));
            results.Add(await store.SaveChangesAsync(default));
        }

        Assert.Equal(1, results.Count(saved => saved));
        Assert.Single((await HistoryAsync(draft.Id)), entry => entry.Kind == HistoryKind.Payment);
        Assert.Single((await ReloadAsync(draft.Id)).Payments);
    }

    // ---- a failed save leaves neither ----

    [Fact]
    public async Task A_save_that_fails_in_the_database_stores_neither_the_change_nor_the_entry()
    {
        var draft = await CreateDraftAsync();
        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var tracked = (await store.FindByIdAsync(draft.Id, default))!;
            Assert.True(tracked.UpdateDetails(tracked.CustomerId, tracked.DueDate, "changed by a save that fails", 0m, DateTime.UtcNow));

            // An entry for an invoice that does not exist: the foreign key refuses it, so the whole save is rolled back.
            store.RecordHistory(InvoiceHistoryEntry.Create(
                Guid.NewGuid(), HistoryKind.DraftEdited, DateTime.UtcNow, "agent1@crm.local", StaffRole.Agent, [new HistoryChange("Notes", "a", "b")]));

            await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(default));
        }

        Assert.Equal("Net 30", (await ReloadAsync(draft.Id)).Notes);
        Assert.Equal(HistoryKind.Created, Assert.Single(await HistoryAsync(draft.Id)).Kind);
    }

    // ---- what is not written ----

    [Fact]
    public async Task An_edit_that_changes_nothing_writes_no_row()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        InvoiceDto created;
        await using (var context = database.CreateContext())
        {
            created = Ok(await NewService(context).CreateAsync(Input(customer, product), default));
        }

        await using (var context = database.CreateContext())
        {
            var same = new InvoiceInput(customer.Id.ToString(), Due, "Net 30", [new InvoiceLineInput(created.Lines[0].Id.ToString(), null, 2m)]);
            Ok(await NewService(context).UpdateAsync(created.Id, same, default));
        }

        Assert.Equal(HistoryKind.Created, Assert.Single(await HistoryAsync(created.Id)).Kind);
    }

    [Fact]
    public async Task A_refused_request_writes_no_row()
    {
        var created = await CreateDraftAsync();

        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).RecordPaymentAsync(created.Id, Pay(10m), default)).IsFailure); // a Draft takes no payment
            Assert.True((await NewService(context).SendAsync(Guid.NewGuid(), default)).IsFailure);
            Assert.True((await NewService(context).CreateAsync(new InvoiceInput(null, "bad", null, []), default)).IsFailure);
        }

        Assert.Equal(HistoryKind.Created, Assert.Single(await HistoryAsync(created.Id)).Kind);
    }

    [Fact]
    public async Task Entries_of_different_invoices_never_mix()
    {
        var first = await CreateDraftAsync();
        var second = await CreateDraftAsync();
        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).SendAsync(first.Id, default));
        }

        Assert.Equal([HistoryKind.Sent, HistoryKind.Created], (await HistoryAsync(first.Id)).Select(entry => entry.Kind));
        Assert.Equal([HistoryKind.Created], (await HistoryAsync(second.Id)).Select(entry => entry.Kind));
        Assert.All(await HistoryAsync(first.Id), entry => Assert.Equal(first.Id, entry.InvoiceId));
    }

    [Fact]
    public async Task Ten_changes_to_one_invoice_list_newest_first_in_sequence_order()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        InvoiceDto created;
        await using (var context = database.CreateContext())
        {
            created = Ok(await NewService(context).CreateAsync(Input(customer, product, "note 0"), default));
        }

        for (var i = 1; i <= 9; i++)
        {
            await using var context = database.CreateContext();
            var edit = new InvoiceInput(customer.Id.ToString(), Due, $"note {i}", [new InvoiceLineInput(created.Lines[0].Id.ToString(), null, 2m)]);
            Ok(await NewService(context).UpdateAsync(created.Id, edit, default));
        }

        var entries = await HistoryAsync(created.Id);

        Assert.Equal(10, entries.Count);
        Assert.Equal(entries.Select(entry => entry.Sequence).OrderByDescending(sequence => sequence), entries.Select(entry => entry.Sequence));
        Assert.Equal(HistoryKind.Created, entries[^1].Kind);
        Assert.Equal("note 9", entries[0].Changes.Single(change => change.Field == "Notes").After);
        Assert.Equal("note 0", entries[^2].Changes.Single(change => change.Field == "Notes").Before);
    }
}
