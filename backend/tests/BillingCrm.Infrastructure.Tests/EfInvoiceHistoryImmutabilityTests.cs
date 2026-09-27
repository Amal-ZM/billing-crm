using BillingCrm.Application.Auth;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Auth;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Persistence;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// History is permanent (spec 010): on real SQL Server no update and no delete of an entry ever succeeds, however it is attempted, while inserting
/// still works, and nothing the application does later changes an entry that was already written.
/// </summary>
public class EfInvoiceHistoryImmutabilityTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private const string Refusal = "Invoice history entries cannot be changed or deleted.";
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Actor Agent = new("agent1@crm.local", StaffRole.Agent);

    private static DateOnly TodaySaudi() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private static string Due => TodaySaudi().AddDays(30).ToString("yyyy-MM-dd");

    private InvoiceService NewService(BillingDbContext context, Actor? actor = null) =>
        new(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser { Current = actor ?? Agent });

    private static InvoiceDto Ok(Result<InvoiceDto> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    private async Task<(Customer Customer, Product Product)> AddCustomerAndProductAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return (customer, product);
    }

    /// <summary>An invoice made through the real service (so it has its Created entry) plus a second entry, and how many entries it has.</summary>
    private async Task<InvoiceDto> CreateWithHistoryAsync(Actor? actor = null)
    {
        var (customer, product) = await AddCustomerAndProductAsync();
        await using var context = database.CreateContext();
        var created = Ok(await NewService(context, actor).CreateAsync(
            new InvoiceInput(customer.Id.ToString(), Due, "Net 30", [new InvoiceLineInput(null, product.Id.ToString(), 2m)]), default));
        return created;
    }

    /// <summary>Every column of every entry of one invoice as one text per row, oldest first: any change to any byte shows.</summary>
    private async Task<List<string>> RowsAsync(Guid? invoiceId = null)
    {
        await using var context = database.CreateContext();
        var where = invoiceId is null ? string.Empty : $"WHERE InvoiceId = '{invoiceId}'";
        return await context.Database.SqlQueryRaw<string>(
                "SELECT CONCAT(Id, '|', Sequence, '|', InvoiceId, '|', Kind, '|', CONVERT(varchar(30), OccurredAtUtc, 126), '|', ActorEmail, '|', ActorRole, '|', ChangesJson) AS [Value] " +
                $"FROM InvoiceHistoryEntries {where} ORDER BY Sequence")
            .ToListAsync();
    }

    private static void AssertRefused(Exception error)
    {
        var sql = error as SqlException ?? error.InnerException as SqlException ?? error.InnerException?.InnerException as SqlException;
        Assert.NotNull(sql);
        Assert.Equal(51000, sql!.Number);
        Assert.Contains(Refusal, sql.Message);
    }

    private async Task AssertRawRefusedAsync(string sql, params object[] parameters)
    {
        await using var context = database.CreateContext();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(sql, parameters));
        AssertRefused(error);
    }

    // ---- update is refused ----

    [Fact]
    public async Task A_raw_update_of_every_row_is_refused_and_every_row_is_unchanged()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync();
        Assert.NotEmpty(before);

        await AssertRawRefusedAsync("UPDATE InvoiceHistoryEntries SET ActorEmail = 'x'");

        Assert.Equal(before, await RowsAsync());
        Assert.NotEmpty(await RowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_raw_update_of_one_row_by_id_is_refused_and_the_row_is_unchanged()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync(invoice.Id);
        var id = before[0].Split('|')[0];

        await AssertRawRefusedAsync($"UPDATE InvoiceHistoryEntries SET ActorEmail = 'x' WHERE Id = '{id}'");

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    [Theory]
    [InlineData("Kind = 'Sent'")]
    [InlineData("ActorRole = 'Admin'")]
    [InlineData("ChangesJson = '[]'")]
    [InlineData("Sequence = 999999")]
    [InlineData("OccurredAtUtc = SYSUTCDATETIME()")]
    [InlineData("InvoiceId = NEWID()")]
    public async Task A_raw_update_of_any_column_is_refused(string assignment)
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync(invoice.Id);

        await AssertRawRefusedAsync($"UPDATE InvoiceHistoryEntries SET {assignment} WHERE InvoiceId = '{invoice.Id}'");

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    // ---- delete is refused ----

    [Fact]
    public async Task A_raw_delete_of_every_row_of_one_row_and_of_one_invoices_rows_is_refused_and_the_rows_remain()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync();
        var id = (await RowsAsync(invoice.Id))[0].Split('|')[0];

        await AssertRawRefusedAsync("DELETE FROM InvoiceHistoryEntries");
        await AssertRawRefusedAsync($"DELETE FROM InvoiceHistoryEntries WHERE Id = '{id}'");
        await AssertRawRefusedAsync($"DELETE FROM InvoiceHistoryEntries WHERE InvoiceId = '{invoice.Id}'");

        Assert.Equal(before, await RowsAsync());
    }

    [Fact]
    public async Task A_statement_that_matches_no_row_is_refused_too_because_the_rule_does_not_depend_on_what_matches()
    {
        // An INSTEAD OF trigger runs once per statement, even when no row matches, so the refusal is unconditional. (TRUNCATE TABLE does not
        // fire triggers at all: it needs ALTER permission on the table, which the application's own sign-in should never hold. Known limit,
        // recorded in research.md and the deferred hardening list.)
        await CreateWithHistoryAsync();
        var before = await RowsAsync();

        await AssertRawRefusedAsync("DELETE FROM InvoiceHistoryEntries WHERE 1 = 0");
        await AssertRawRefusedAsync("UPDATE InvoiceHistoryEntries SET ActorEmail = ActorEmail WHERE 1 = 0");

        Assert.Equal(before, await RowsAsync());
    }

    // ---- through EF ----

    [Fact]
    public async Task ExecuteUpdate_and_ExecuteDelete_through_EF_fail_and_change_nothing()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var update = await Assert.ThrowsAnyAsync<Exception>(() =>
                context.InvoiceHistoryEntries.Where(entry => entry.InvoiceId == invoice.Id).ExecuteUpdateAsync(set => set.SetProperty(entry => entry.ActorEmail, "x")));
            AssertRefused(update);
        }

        await using (var context = database.CreateContext())
        {
            var delete = await Assert.ThrowsAnyAsync<Exception>(() =>
                context.InvoiceHistoryEntries.Where(entry => entry.InvoiceId == invoice.Id).ExecuteDeleteAsync());
            AssertRefused(delete);
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_tracked_entry_edited_and_saved_fails_and_changes_nothing()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var entry = await context.InvoiceHistoryEntries.FirstAsync(candidate => candidate.InvoiceId == invoice.Id);
            context.Entry(entry).Property(candidate => candidate.ActorEmail).CurrentValue = "x@crm.local";

            var error = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            AssertRefused(error);
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_tracked_entry_removed_and_saved_fails_and_the_row_remains()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var entry = await context.InvoiceHistoryEntries.FirstAsync(candidate => candidate.InvoiceId == invoice.Id);
            context.InvoiceHistoryEntries.Remove(entry);

            var error = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            AssertRefused(error);
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    // ---- insert still works ----

    [Fact]
    public async Task An_insert_still_works()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = (await RowsAsync(invoice.Id)).Count;

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            store.RecordHistory(InvoiceHistoryEntry.Create(invoice.Id, HistoryKind.DraftEdited, DateTime.UtcNow, "agent1@crm.local", StaffRole.Agent, [new HistoryChange("Notes", "a", "b")]));
            Assert.True(await store.SaveChangesAsync(default));
        }

        Assert.Equal(before + 1, (await RowsAsync(invoice.Id)).Count);
    }

    // ---- the invoice cannot take its history with it ----

    [Fact]
    public async Task Deleting_an_invoice_that_has_entries_is_refused_and_the_entries_remain()
    {
        var invoice = await CreateWithHistoryAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                context.Database.ExecuteSqlRawAsync("DELETE FROM Invoices WHERE Id = {0}", invoice.Id));
            Assert.Contains("FK_InvoiceHistoryEntries_Invoices_InvoiceId", error.Message);
        }

        await using (var context = database.CreateContext())
        {
            var tracked = await new EfInvoiceStore(context).FindByIdAsync(invoice.Id, default);
            Assert.NotNull(tracked);
            context.Invoices.Remove(tracked!);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
        await using var check = database.CreateContext();
        Assert.NotNull(await new EfInvoiceStore(check).FindByIdAsync(invoice.Id, default));
    }

    // ---- what the application does later never touches an earlier entry ----

    [Fact]
    public async Task Later_edits_a_send_payments_and_a_cancel_leave_earlier_entries_byte_for_byte_the_same_and_only_add_new_ones()
    {
        var created = await CreateWithHistoryAsync();
        var snapshots = new List<List<string>> { await RowsAsync(created.Id) };

        await using (var context = database.CreateContext())
        {
            var loaded = (await new EfInvoiceStore(context).FindByIdAsync(created.Id, default))!;
            var edit = new InvoiceInput(loaded.CustomerId.ToString(), Due, "Pay on receipt", [new InvoiceLineInput(loaded.Lines[0].Id.ToString(), null, 3m)]);
            Ok(await NewService(context).UpdateAsync(created.Id, edit, default));
        }

        snapshots.Add(await RowsAsync(created.Id));

        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).SendAsync(created.Id, default));
        }

        snapshots.Add(await RowsAsync(created.Id));

        var pay = new PaymentInput(TodaySaudi().ToString("yyyy-MM-dd"), 100m, "Cash", null);
        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).RecordPaymentAsync(created.Id, pay, default));
        }

        snapshots.Add(await RowsAsync(created.Id));

        // A second invoice, cancelled by an Admin, to cover the cancel path (an invoice with a payment cannot be cancelled).
        var other = await CreateWithHistoryAsync();
        var otherBefore = await RowsAsync(other.Id);
        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context, new Actor("admin@crm.local", StaffRole.Admin)).CancelAsync(other.Id, default));
        }

        var otherAfter = await RowsAsync(other.Id);

        for (var i = 1; i < snapshots.Count; i++)
        {
            Assert.Equal(snapshots[i - 1], snapshots[i].Take(snapshots[i - 1].Count).ToList());
            Assert.Equal(snapshots[i - 1].Count + 1, snapshots[i].Count);
        }

        Assert.Equal(otherBefore, otherAfter.Take(otherBefore.Count).ToList());
        Assert.Equal(otherBefore.Count + 1, otherAfter.Count);
    }

    // ---- values as at the time, on real SQL ----

    [Fact]
    public async Task The_Created_entry_still_reads_the_old_customer_name_and_price_after_they_change_and_the_product_is_deactivated()
    {
        var (customer, product) = await AddCustomerAndProductAsync();
        InvoiceDto created;
        await using (var context = database.CreateContext())
        {
            created = Ok(await NewService(context).CreateAsync(
                new InvoiceInput(customer.Id.ToString(), Due, null, [new InvoiceLineInput(null, product.Id.ToString(), 2m)]), default));
        }

        var before = await RowsAsync(created.Id);

        await using (var context = database.CreateContext())
        {
            var customers = new EfCustomerStore(context);
            var tracked = (await customers.FindByIdAsync(customer.Id, default))!;
            Assert.True(tracked.Update("ABC Renamed", CustomerType.Company, tracked.Email, tracked.Phone, null, tracked.Address, DateTime.UtcNow));
            Assert.True(await customers.TryUpdateAsync(tracked, default));

            var products = new EfProductStore(context);
            var trackedProduct = (await products.FindByIdAsync(product.Id, default))!;
            Assert.True(trackedProduct.Update("Consulting hour (renamed)", trackedProduct.Sku, ProductUnit.Hour, 999m, 5m, DateTime.UtcNow));
            Assert.True(trackedProduct.SetActive(false, DateTime.UtcNow));
            Assert.True(await products.TryUpdateAsync(trackedProduct, default));
        }

        Assert.Equal(before, await RowsAsync(created.Id));
        await using var read = database.CreateContext();
        var entry = Assert.Single(await new EfInvoiceStore(read).ListHistoryAsync(created.Id, default));
        Assert.Contains(new HistoryChange("Customer", null, "ABC"), entry.Changes);
        Assert.Contains(new HistoryChange("Line: Consulting hour (" + product.Sku + ")", null, "2.00 x SAR 100.00, VAT 15.00%"), entry.Changes);
    }

    [Fact]
    public async Task An_entry_keeps_the_email_and_role_it_was_written_with_when_the_staff_users_row_changes_or_goes_away()
    {
        var user = StaffUser.Create($"{Guid.NewGuid():N}@crm.local", "hash-not-used", StaffRole.Agent);
        await using (var context = database.CreateContext())
        {
            await new EfStaffUserStore(context).AddAsync(user, default);
        }

        var created = await CreateWithHistoryAsync(new Actor(user.Email, StaffRole.Agent));
        var before = await RowsAsync(created.Id);
        Assert.Contains($"|{user.Email}|Agent|", before[0]);

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE StaffUsers SET Email = {0}, NormalizedEmail = {0}, Role = 'Admin' WHERE Id = {1}", "renamed@crm.local", user.Id);
        }

        Assert.Equal(before, await RowsAsync(created.Id));

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM StaffUsers WHERE Id = {0}", user.Id);
        }

        Assert.Equal(before, await RowsAsync(created.Id));
    }
}
