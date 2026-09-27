using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Invoice history on real SQL Server (spec 010): what is stored, how it reads back and what the database itself refuses.</summary>
public class EfInvoiceHistoryStorageTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task<Invoice> AddInvoiceAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        }

        var invoice = Invoice.Create(customer.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, Now);
        await using (var context = database.CreateContext())
        {
            await new EfInvoiceStore(context).AddAsync(invoice, default);
        }

        return invoice;
    }

    private static InvoiceHistoryEntry Entry(
        Guid invoiceId,
        HistoryKind kind = HistoryKind.DraftEdited,
        string email = "agent1@crm.local",
        StaffRole role = StaffRole.Agent,
        params HistoryChange[] changes) =>
        InvoiceHistoryEntry.Create(invoiceId, kind, DateTime.UtcNow, email, role, changes.Length == 0 ? [new HistoryChange("Notes", "a", "b")] : changes);

    private async Task WriteAsync(params InvoiceHistoryEntry[] entries)
    {
        await using var context = database.CreateContext();
        var store = new EfInvoiceStore(context);
        foreach (var entry in entries)
        {
            store.RecordHistory(entry);
        }

        Assert.True(await store.SaveChangesAsync(default));
    }

    private async Task<IReadOnlyList<InvoiceHistoryEntry>> ListAsync(Guid invoiceId)
    {
        await using var context = database.CreateContext();
        return await new EfInvoiceStore(context).ListHistoryAsync(invoiceId, default);
    }

    private async Task<SqlException> RawInsertFailsAsync(string kind, string role, string changesJson, Guid invoiceId)
    {
        await using var context = database.CreateContext();
        return await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO InvoiceHistoryEntries (Id, InvoiceId, Kind, OccurredAtUtc, ActorEmail, ActorRole, ChangesJson) " +
            "VALUES ({0}, {1}, {2}, SYSUTCDATETIME(), 'agent1@crm.local', {3}, {4})",
            Guid.NewGuid(), invoiceId, kind, role, changesJson));
    }

    // ---- round trip ----

    [Fact]
    public async Task An_entry_reads_back_with_its_kind_utc_time_user_role_and_changes()
    {
        var invoice = await AddInvoiceAsync();
        var entry = Entry(invoice.Id, HistoryKind.Sent, "admin@crm.local", StaffRole.Admin,
            new HistoryChange("Status", "Draft", "Sent"), new HistoryChange("Notes", null, "new"), new HistoryChange("Due date", "2026-01-01", null));

        await WriteAsync(entry);

        var stored = Assert.Single(await ListAsync(invoice.Id));
        Assert.Equal(entry.Id, stored.Id);
        Assert.Equal(invoice.Id, stored.InvoiceId);
        Assert.Equal(HistoryKind.Sent, stored.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.OccurredAtUtc.Kind);
        Assert.True((stored.OccurredAtUtc - entry.OccurredAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.Equal("admin@crm.local", stored.ActorEmail);
        Assert.Equal(StaffRole.Admin, stored.ActorRole);
        Assert.Equal(entry.Changes, stored.Changes);
    }

    [Fact]
    public async Task Arabic_english_null_sides_a_long_value_and_many_changes_survive_intact()
    {
        var invoice = await AddInvoiceAsync();
        var changes = new List<HistoryChange>
        {
            new("Notes", "ملاحظة قديمة", "new note / ملاحظة جديدة"),
            new("Line added: خدمة (SVC-1)", null, "1.00"),
            new("Line removed: Hour (H-1)", "2.00", null),
            new("Long", new string('س', 1000), new string('x', 1000)),
        };
        changes.AddRange(Enumerable.Range(0, 96).Select(i => new HistoryChange($"Field {i}", i.ToString(), (i + 1).ToString())));
        Assert.Equal(100, changes.Count);

        await WriteAsync(InvoiceHistoryEntry.Create(invoice.Id, HistoryKind.DraftEdited, DateTime.UtcNow, "agent1@crm.local", StaffRole.Agent, changes));

        var stored = Assert.Single(await ListAsync(invoice.Id));
        Assert.Equal(changes, stored.Changes);
    }

    [Fact]
    public async Task Sequence_is_filled_by_the_database_and_increases()
    {
        var invoice = await AddInvoiceAsync();

        await WriteAsync(Entry(invoice.Id, HistoryKind.Created));
        await WriteAsync(Entry(invoice.Id, HistoryKind.DraftEdited));

        var list = await ListAsync(invoice.Id);
        Assert.Equal(2, list.Count);
        Assert.All(list, entry => Assert.True(entry.Sequence > 0));
        Assert.True(list[0].Sequence > list[1].Sequence);
    }

    [Fact]
    public async Task An_entry_recorded_before_AddAsync_is_written_with_the_new_invoice_in_one_save()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        }

        var invoice = Invoice.Create(customer.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, Now);
        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            store.RecordHistory(Entry(invoice.Id, HistoryKind.Created, changes: new HistoryChange("Customer", null, "ABC")));
            await store.AddAsync(invoice, default);
        }

        var stored = Assert.Single(await ListAsync(invoice.Id));
        Assert.Equal(HistoryKind.Created, stored.Kind);
    }

    // ---- reading ----

    [Fact]
    public async Task ListHistory_returns_only_that_invoices_entries_newest_first()
    {
        var first = await AddInvoiceAsync();
        var second = await AddInvoiceAsync();
        var oldest = Entry(first.Id, HistoryKind.Created);
        var middle = Entry(second.Id, HistoryKind.Created);
        var newest = Entry(first.Id, HistoryKind.Sent);

        await WriteAsync(oldest);
        await WriteAsync(middle);
        await WriteAsync(newest);

        Assert.Equal([newest.Id, oldest.Id], (await ListAsync(first.Id)).Select(entry => entry.Id));
        Assert.Equal([middle.Id], (await ListAsync(second.Id)).Select(entry => entry.Id));
    }

    [Fact]
    public async Task ListHistory_is_empty_for_an_invoice_with_no_entries_and_for_an_unknown_id()
    {
        var invoice = await AddInvoiceAsync();

        Assert.Empty(await ListAsync(invoice.Id));
        Assert.Empty(await ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListHistory_does_not_track_what_it_reads()
    {
        var invoice = await AddInvoiceAsync();
        await WriteAsync(Entry(invoice.Id));

        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).ListHistoryAsync(invoice.Id, default);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task FindNumber_returns_the_number_or_null_for_an_unknown_id()
    {
        var invoice = await AddInvoiceAsync();
        await using var context = database.CreateContext();
        var store = new EfInvoiceStore(context);

        Assert.Equal(invoice.Number, await store.FindNumberAsync(invoice.Id, default));
        Assert.Null(await store.FindNumberAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Loading_an_invoice_does_not_load_its_history()
    {
        var invoice = await AddInvoiceAsync();
        await WriteAsync(Entry(invoice.Id));

        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).FindByIdAsync(invoice.Id, default);

        Assert.Empty(context.ChangeTracker.Entries<InvoiceHistoryEntry>());
    }

    // ---- what the database refuses ----

    [Fact]
    public async Task A_raw_insert_with_an_unknown_kind_is_refused()
    {
        var invoice = await AddInvoiceAsync();

        var error = await RawInsertFailsAsync("Deleted", "Agent", "[{\"field\":\"a\"}]", invoice.Id);

        Assert.Contains("CK_InvoiceHistoryEntries_Kind", error.Message);
    }

    [Fact]
    public async Task A_raw_insert_with_an_unknown_role_is_refused()
    {
        var invoice = await AddInvoiceAsync();

        var error = await RawInsertFailsAsync("Sent", "Manager", "[{\"field\":\"a\"}]", invoice.Id);

        Assert.Contains("CK_InvoiceHistoryEntries_ActorRole", error.Message);
    }

    [Fact]
    public async Task A_raw_insert_with_malformed_changes_text_is_refused()
    {
        var invoice = await AddInvoiceAsync();

        var error = await RawInsertFailsAsync("Sent", "Agent", "not json {", invoice.Id);

        Assert.Contains("CK_InvoiceHistoryEntries_ChangesJson", error.Message);
    }

    [Fact]
    public async Task A_raw_insert_for_an_unknown_invoice_is_refused()
    {
        var error = await RawInsertFailsAsync("Sent", "Agent", "[{\"field\":\"a\"}]", Guid.NewGuid());

        Assert.Contains("FK_InvoiceHistoryEntries_Invoices_InvoiceId", error.Message);
    }
}
