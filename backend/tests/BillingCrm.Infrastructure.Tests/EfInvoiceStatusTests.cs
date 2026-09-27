using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Persistence;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Status changes on real SQL Server (spec 007): what is stored, the consistency checks and the row version's races.</summary>
public class EfInvoiceStatusTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Due = new(2030, 7, 1);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Customer> AddCustomerAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync(decimal price = 100m, decimal vat = 15m)
    {
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, price, vat, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    /// <summary>A stored Draft (consulting x 2, 230.00) with the given due date.</summary>
    private async Task<(Invoice Invoice, Customer Customer, Product Product)> AddDraftAsync(DateOnly? due = null, decimal discount = 0m)
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var invoice = Invoice.Create(customer.Id, due ?? Due, "Net 30", Now, discount);
        invoice.AddLine(product, 2m, Now);
        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).AddAsync(invoice, default);
        return (invoice, customer, product);
    }

    private InvoiceService NewService(BillingDbContext context) =>
        new(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser());

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return (await new EfInvoiceStore(context).FindByIdAsync(id, default))!;
    }

    private async Task<SqlException> InsertRawAsync(string status, string? sentAt, string? cancelledAt, string? frozenTotal)
    {
        var customer = await AddCustomerAsync();
        await using var context = database.CreateContext();
        var frozen = frozenTotal is null
            ? "NULL, NULL, NULL, NULL"
            : $"{frozenTotal}, {frozenTotal}, 0, {frozenTotal}";
        return await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, CustomerId, Status, DueDate, CreatedAtUtc, UpdatedAtUtc, SentAtUtc, CancelledAtUtc, " +
            "FrozenTotalNet, FrozenNetAfterDiscount, FrozenTotalVat, FrozenGrandTotal) " +
            $"VALUES (NEWID(), @customer, @status, '2030-07-01', SYSUTCDATETIME(), SYSUTCDATETIME(), {sentAt ?? "NULL"}, {cancelledAt ?? "NULL"}, {frozen})",
            new SqlParameter("customer", customer.Id),
            new SqlParameter("status", status)));
    }

    private async Task InsertRawOkAsync(string status, string? sentAt, string? cancelledAt, string? frozenTotal)
    {
        var customer = await AddCustomerAsync();
        await using var context = database.CreateContext();
        var frozen = frozenTotal is null
            ? "NULL, NULL, NULL, NULL"
            : $"{frozenTotal}, {frozenTotal}, 0, {frozenTotal}";
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, CustomerId, Status, DueDate, CreatedAtUtc, UpdatedAtUtc, SentAtUtc, CancelledAtUtc, " +
            "FrozenTotalNet, FrozenNetAfterDiscount, FrozenTotalVat, FrozenGrandTotal) " +
            $"VALUES (NEWID(), @customer, @status, '2030-07-01', SYSUTCDATETIME(), SYSUTCDATETIME(), {sentAt ?? "NULL"}, {cancelledAt ?? "NULL"}, {frozen})",
            new SqlParameter("customer", customer.Id),
            new SqlParameter("status", status));
    }

    // ---- send round trip ----

    [Fact]
    public async Task A_Draft_sent_through_the_real_service_reads_back_Sent_with_the_time_and_the_frozen_amounts()
    {
        var (invoice, _, _) = await AddDraftAsync(discount: 20.00m);

        await using (var context = database.CreateContext())
        {
            var sent = await NewService(context).SendAsync(invoice.Id, default);
            Assert.True(sent.IsSuccess);
            Assert.Equal("Sent", sent.Value.Status);
        }

        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Sent, loaded.Status);
        Assert.NotNull(loaded.SentAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.SentAtUtc!.Value.Kind);
        Assert.Null(loaded.CancelledAtUtc);
        Assert.Equal(200.00m, loaded.FrozenTotalNet);
        Assert.Equal(180.00m, loaded.FrozenNetAfterDiscount);
        Assert.Equal(27.00m, loaded.FrozenTotalVat);
        Assert.Equal(207.00m, loaded.FrozenGrandTotal);
        var line = Assert.Single(loaded.Lines);
        Assert.Equal((200.00m, 20.00m, 27.00m), (line.FrozenNet, line.FrozenDiscountShare, line.FrozenVat));
        Assert.Equal(207.00m, loaded.Amounts.GrandTotal);
    }

    [Fact]
    public async Task A_repriced_product_does_not_change_a_Sent_invoices_amounts_read_from_the_database()
    {
        var (invoice, _, product) = await AddDraftAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).SendAsync(invoice.Id, default)).IsSuccess);
        }

        await using (var writer = database.CreateContext())
        {
            await writer.Database.ExecuteSqlRawAsync(
                "UPDATE Products SET UnitPrice = 999.99, VatPercent = 0 WHERE Id = {0}", product.Id);
        }

        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(230.00m, loaded.Amounts.GrandTotal);
        Assert.Equal(200.00m, loaded.Amounts.Lines[0].Net);
    }

    [Fact]
    public async Task A_Sent_invoice_reads_its_frozen_amounts_even_if_the_stored_line_values_are_changed_directly_in_the_database()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).SendAsync(invoice.Id, default)).IsSuccess);
        }

        // Not what a product change does (that never reaches a line); this proves the read really uses the frozen columns.
        await using (var writer = database.CreateContext())
        {
            await writer.Database.ExecuteSqlRawAsync("UPDATE InvoiceLines SET UnitPrice = 999.99, Quantity = 50 WHERE InvoiceId = {0}", invoice.Id);
        }

        await using var reader = database.CreateContext();
        var opened = (await NewService(reader).GetAsync(invoice.Id, default)).Value;
        Assert.Equal(230.00m, opened.Totals.GrandTotal);
        Assert.Equal(200.00m, opened.Lines[0].Net);
        Assert.Equal(30.00m, opened.Lines[0].Vat);
    }

    [Fact]
    public async Task A_line_only_edit_that_reuses_the_invoices_own_changed_time_still_loses_to_a_send_that_committed_first()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using var editContext = database.CreateContext();
        await using var sendContext = database.CreateContext();
        var editStore = new EfInvoiceStore(editContext);
        var sendStore = new EfInvoiceStore(sendContext);
        var editCopy = (await editStore.FindByIdAsync(invoice.Id, default))!;
        var sendCopy = (await sendStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(sendCopy.Send(Now.AddHours(1)));
        Assert.True(await sendStore.SaveChangesAsync(default));
        // The same changed-time the invoice already has: without help, EF would see no change to the invoice row and skip the version check.
        Assert.True(editCopy.SetLineQuantity(editCopy.Lines[0].Id, 9m, editCopy.UpdatedAtUtc));
        var saved = await editStore.SaveChangesAsync(default);

        Assert.False(saved);
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Sent, loaded.Status);
        Assert.Equal(2m, loaded.Lines.Single().Quantity);
    }

    // ---- the consistency checks ----

    [Theory]
    [InlineData("Sent", null, null, "230")]                                     // Sent with no time
    [InlineData("Sent", "SYSUTCDATETIME()", null, null)]                          // Sent with no frozen amounts
    [InlineData("Draft", "SYSUTCDATETIME()", null, null)]                         // a Draft that has a sent time
    [InlineData("Draft", null, null, "230")]                                    // a Draft with frozen amounts
    [InlineData("Cancelled", null, null, "230")]                                // Cancelled with no cancelled time
    [InlineData("Paid", null, null, "230")]                                     // Paid with no sent time
    [InlineData("Sent", "SYSUTCDATETIME()", "SYSUTCDATETIME()", "230")]         // Sent and cancelled at once
    [InlineData("Overdue", "SYSUTCDATETIME()", null, "230")]                    // Overdue is derived, never a stored status
    public async Task The_checks_refuse_a_row_whose_times_or_frozen_amounts_do_not_match_its_status(string status, string? sentAt, string? cancelledAt, string? frozen)
    {
        var exception = await InsertRawAsync(status, sentAt, cancelledAt, frozen);

        Assert.Equal(547, exception.Number);
        Assert.Contains("CK_Invoices_", exception.Message);
    }

    [Fact]
    public async Task The_checks_accept_every_consistent_shape()
    {
        await InsertRawOkAsync("Draft", null, null, null);
        await InsertRawOkAsync("Sent", "SYSUTCDATETIME()", null, "230");
        await InsertRawOkAsync("Paid", "SYSUTCDATETIME()", null, "230");
        await InsertRawOkAsync("Cancelled", null, "SYSUTCDATETIME()", "230");                       // cancelled straight from Draft
        await InsertRawOkAsync("Cancelled", "SYSUTCDATETIME()", "SYSUTCDATETIME()", "230");         // cancelled after being sent
    }

    // ---- races: exactly one winner ----

    [Fact]
    public async Task Two_contexts_that_both_send_the_same_Draft_have_exactly_one_winner()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(invoice.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(firstCopy.Send(Now.AddHours(1)));
        Assert.True(secondCopy.Send(Now.AddHours(2)));
        var results = new[] { await firstStore.SaveChangesAsync(default), await secondStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Sent, loaded.Status);
        Assert.Equal(Now.AddHours(1), loaded.SentAtUtc);
    }

    [Fact]
    public async Task An_edit_that_loaded_the_Draft_before_it_was_sent_cannot_change_a_line_afterwards()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using var editContext = database.CreateContext();
        await using var sendContext = database.CreateContext();
        var editStore = new EfInvoiceStore(editContext);
        var sendStore = new EfInvoiceStore(sendContext);
        var editCopy = (await editStore.FindByIdAsync(invoice.Id, default))!;
        var sendCopy = (await sendStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(sendCopy.Send(Now.AddHours(1)));
        Assert.True(await sendStore.SaveChangesAsync(default));
        Assert.True(editCopy.SetLineQuantity(editCopy.Lines[0].Id, 9m, Now.AddHours(2)));
        var saved = await editStore.SaveChangesAsync(default);

        Assert.False(saved);
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Sent, loaded.Status);
        Assert.Equal(2m, loaded.Lines.Single().Quantity);
        Assert.Equal(230.00m, loaded.Amounts.GrandTotal);
    }

    [Fact]
    public async Task Two_contexts_that_both_change_the_same_Draft_have_exactly_one_winner_and_no_last_save_wins()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(invoice.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(firstCopy.SetLineQuantity(firstCopy.Lines[0].Id, 3m, Now.AddHours(1)));
        Assert.True(secondCopy.SetLineQuantity(secondCopy.Lines[0].Id, 7m, Now.AddHours(2)));
        var results = new[] { await firstStore.SaveChangesAsync(default), await secondStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Draft, loaded.Status);
        Assert.Equal(results[0] ? 3m : 7m, loaded.Lines.Single().Quantity);
    }

    // ---- cancel round trip ----

    [Fact]
    public async Task Cancelling_a_Draft_through_the_real_service_stores_the_time_and_the_frozen_amounts_and_no_sent_time()
    {
        var (invoice, _, _) = await AddDraftAsync(discount: 20.00m);

        await using (var context = database.CreateContext())
        {
            var cancelled = await NewService(context).CancelAsync(invoice.Id, default);
            Assert.True(cancelled.IsSuccess);
            Assert.Equal("Cancelled", cancelled.Value.Status);
        }

        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Cancelled, loaded.Status);
        Assert.NotNull(loaded.CancelledAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.CancelledAtUtc!.Value.Kind);
        Assert.Null(loaded.SentAtUtc);
        Assert.Equal(207.00m, loaded.FrozenGrandTotal);
        Assert.Equal(207.00m, loaded.Amounts.GrandTotal);
        Assert.Single(loaded.Lines);
        Assert.True(loaded.Number >= 1);
    }

    [Fact]
    public async Task Cancelling_a_Sent_invoice_keeps_the_sent_time_and_the_frozen_amounts()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).SendAsync(invoice.Id, default)).IsSuccess);
        }

        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).CancelAsync(invoice.Id, default)).IsSuccess);
        }

        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Cancelled, loaded.Status);
        Assert.NotNull(loaded.SentAtUtc);
        Assert.NotNull(loaded.CancelledAtUtc);
        Assert.Equal(230.00m, loaded.FrozenGrandTotal);
    }

    [Fact]
    public async Task Two_contexts_that_both_cancel_the_same_Sent_invoice_have_exactly_one_winner()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).SendAsync(invoice.Id, default)).IsSuccess);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(invoice.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(firstCopy.Cancel(Now.AddHours(1)));
        Assert.True(secondCopy.Cancel(Now.AddHours(2)));
        var results = new[] { await firstStore.SaveChangesAsync(default), await secondStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        Assert.Equal(Now.AddHours(1), (await ReloadAsync(invoice.Id)).CancelledAtUtc);
    }

    [Fact]
    public async Task A_send_racing_a_cancel_of_the_same_Draft_has_exactly_one_winner()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using var sendContext = database.CreateContext();
        await using var cancelContext = database.CreateContext();
        var sendStore = new EfInvoiceStore(sendContext);
        var cancelStore = new EfInvoiceStore(cancelContext);
        var sendCopy = (await sendStore.FindByIdAsync(invoice.Id, default))!;
        var cancelCopy = (await cancelStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(sendCopy.Send(Now.AddHours(1)));
        Assert.True(cancelCopy.Cancel(Now.AddHours(2)));
        var results = new[] { await sendStore.SaveChangesAsync(default), await cancelStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        var loaded = await ReloadAsync(invoice.Id);
        Assert.True(loaded.Status is InvoiceStatus.Sent or InvoiceStatus.Cancelled);
        Assert.Equal(230.00m, loaded.Amounts.GrandTotal);
    }

    [Fact]
    public async Task A_send_that_loaded_the_Draft_before_a_line_was_edited_is_refused_and_the_invoice_stays_an_edited_Draft()
    {
        var (invoice, _, _) = await AddDraftAsync();
        await using var editContext = database.CreateContext();
        await using var sendContext = database.CreateContext();
        var editStore = new EfInvoiceStore(editContext);
        var sendStore = new EfInvoiceStore(sendContext);
        var editCopy = (await editStore.FindByIdAsync(invoice.Id, default))!;
        var sendCopy = (await sendStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(editCopy.SetLineQuantity(editCopy.Lines[0].Id, 9m, Now.AddHours(1)));
        Assert.True(await editStore.SaveChangesAsync(default));
        Assert.True(sendCopy.Send(Now.AddHours(2)));
        var saved = await sendStore.SaveChangesAsync(default);

        Assert.False(saved);
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Draft, loaded.Status);
        Assert.Null(loaded.SentAtUtc);
        Assert.Equal(9m, loaded.Lines.Single().Quantity);
    }
}
