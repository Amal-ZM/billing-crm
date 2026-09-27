using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Persistence;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Payments on real SQL Server (spec 008): what is stored, the settle-in-one-save rule and the races.</summary>
public class EfPaymentTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static DateOnly TodaySaudi() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Customer> AddCustomerAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
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

    /// <summary>A stored Sent invoice (consulting x 2, grand total 230.00), sent now.</summary>
    private async Task<(Invoice Invoice, Product Product)> AddSentAsync()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var invoice = Invoice.Create(customer.Id, TodaySaudi().AddDays(30), "Net 30", Now);
        invoice.AddLine(product, 2m, Now);
        Assert.True(invoice.Send(Now));
        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).AddAsync(invoice, default);
        return (invoice, product);
    }

    private InvoiceService NewService(BillingDbContext context) =>
        new(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser());

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return (await new EfInvoiceStore(context).FindByIdAsync(id, default))!;
    }

    private async Task<int> PaymentRowsAsync(Guid invoiceId)
    {
        await using var context = database.CreateContext();
        return await context.Payments.CountAsync(payment => payment.InvoiceId == invoiceId);
    }

    private static PaymentInput Pay(decimal amount, string method = "Cash", string? note = null) =>
        new(TodaySaudi().ToString("yyyy-MM-dd"), amount, method, note);

    // ---- round trip ----

    [Fact]
    public async Task A_saved_payment_round_trips_its_date_exact_amount_method_and_note_including_Arabic()
    {
        var (invoice, _) = await AddSentAsync();

        await using (var context = database.CreateContext())
        {
            var result = await NewService(context).RecordPaymentAsync(invoice.Id, Pay(100.10m, "Bank", "دفعة أولى / first half"), default);
            Assert.True(result.IsSuccess, result.Error?.Detail);
        }

        var loaded = await ReloadAsync(invoice.Id);
        var payment = Assert.Single(loaded.Payments);
        Assert.Equal(invoice.Id, payment.InvoiceId);
        Assert.Equal(TodaySaudi(), payment.PaidOn);
        Assert.Equal(100.10m, payment.Amount);
        Assert.Equal(PaymentMethod.Bank, payment.Method);
        Assert.Equal("دفعة أولى / first half", payment.Note);
        Assert.Equal(DateTimeKind.Utc, payment.RecordedAtUtc.Kind);
        Assert.Equal(100.10m, loaded.Paid);
        Assert.Equal(129.90m, loaded.Remaining);
        Assert.Equal(InvoiceStatus.Sent, loaded.Status);
    }

    [Fact]
    public async Task A_payment_without_a_note_is_stored_with_no_note()
    {
        var (invoice, _) = await AddSentAsync();

        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).RecordPaymentAsync(invoice.Id, Pay(10.00m), default)).IsSuccess);
        }

        Assert.Null((await ReloadAsync(invoice.Id)).Payments.Single().Note);
    }

    [Fact]
    public async Task An_invoice_loads_with_its_lines_and_its_payments_in_the_order_they_were_recorded()
    {
        var (invoice, _) = await AddSentAsync();
        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var tracked = (await store.FindByIdAsync(invoice.Id, default))!;
            var date = TodaySaudi();
            tracked.RecordPayment(date, 20.00m, PaymentMethod.Bank, "second", Now.AddMinutes(2));
            tracked.RecordPayment(date, 10.00m, PaymentMethod.Cash, "first", Now.AddMinutes(1));
            Assert.True(await store.SaveChangesAsync(default));
        }

        var loaded = await ReloadAsync(invoice.Id);

        Assert.Single(loaded.Lines);
        Assert.Equal(["first", "second"], loaded.Payments.Select(payment => payment.Note));
        Assert.Equal(30.00m, loaded.Paid);
        Assert.Equal(200.00m, loaded.Remaining);
    }

    // ---- settling ----

    [Fact]
    public async Task The_payment_that_settles_the_invoice_is_saved_together_with_the_Paid_status()
    {
        var (invoice, _) = await AddSentAsync();

        await using (var context = database.CreateContext())
        {
            var service = NewService(context);
            Assert.True((await service.RecordPaymentAsync(invoice.Id, Pay(100.00m), default)).IsSuccess);
            var settled = await service.RecordPaymentAsync(invoice.Id, Pay(130.00m, "Bank"), default);
            Assert.True(settled.IsSuccess, settled.Error?.Detail);
            Assert.Equal("Paid", settled.Value.Status);
        }

        // A separate context sees both the status and the payments: the payment and the status were saved together (atomicity under a lost race is shown by the two-contexts settle test below).
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, loaded.Status);
        Assert.Equal(2, loaded.Payments.Count);
        Assert.Equal(0.00m, loaded.Remaining);
        Assert.Equal(2, await PaymentRowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_refused_payment_writes_no_row_and_leaves_the_invoice_alone()
    {
        var (invoice, _) = await AddSentAsync();

        await using (var context = database.CreateContext())
        {
            var result = await NewService(context).RecordPaymentAsync(invoice.Id, Pay(230.01m), default);
            Assert.True(result.IsFailure);
        }

        Assert.Equal(0, await PaymentRowsAsync(invoice.Id));
        Assert.Equal(InvoiceStatus.Sent, (await ReloadAsync(invoice.Id)).Status);
    }

    // ---- races ----

    [Fact]
    public async Task Two_contexts_that_both_pay_from_the_same_balance_have_exactly_one_winner_and_the_invoice_is_never_overpaid()
    {
        var (invoice, _) = await AddSentAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(invoice.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(invoice.Id, default))!;
        var date = TodaySaudi();

        // Each is fine on its own (200 of 230); together they would be 400.
        firstCopy.RecordPayment(date, 200.00m, PaymentMethod.Cash, "first", Now.AddMinutes(1));
        secondCopy.RecordPayment(date, 200.00m, PaymentMethod.Bank, "second", Now.AddMinutes(2));
        var results = new[] { await firstStore.SaveChangesAsync(default), await secondStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Single(loaded.Payments);
        Assert.Equal(200.00m, loaded.Paid);
        Assert.Equal(30.00m, loaded.Remaining);
        Assert.Equal(1, await PaymentRowsAsync(invoice.Id));
    }

    [Fact]
    public async Task Two_contexts_that_each_would_settle_the_invoice_cannot_both_be_saved()
    {
        var (invoice, _) = await AddSentAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(invoice.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(invoice.Id, default))!;

        firstCopy.RecordPayment(TodaySaudi(), 230.00m, PaymentMethod.Cash, null, Now.AddMinutes(1));
        secondCopy.RecordPayment(TodaySaudi(), 230.00m, PaymentMethod.Bank, null, Now.AddMinutes(2));
        var results = new[] { await firstStore.SaveChangesAsync(default), await secondStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, loaded.Status);
        Assert.Equal(230.00m, loaded.Paid);
        Assert.Single(loaded.Payments);
    }

    [Fact]
    public async Task A_payment_recorded_at_the_same_instant_as_another_writer_still_loses_the_row_version_check()
    {
        // Both copies use the very same changed-time as the stored invoice, so only the touched invoice row (row version) can stop the second.
        var (invoice, _) = await AddSentAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStore = new EfInvoiceStore(first);
        var secondStore = new EfInvoiceStore(second);
        var firstCopy = (await firstStore.FindByIdAsync(invoice.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(invoice.Id, default))!;
        var sameInstant = firstCopy.UpdatedAtUtc;

        firstCopy.RecordPayment(TodaySaudi(), 10.00m, PaymentMethod.Cash, null, sameInstant);
        secondCopy.RecordPayment(TodaySaudi(), 20.00m, PaymentMethod.Cash, null, sameInstant);
        var results = new[] { await firstStore.SaveChangesAsync(default), await secondStore.SaveChangesAsync(default) };

        Assert.Equal(1, results.Count(saved => saved));
        Assert.Single((await ReloadAsync(invoice.Id)).Payments);
    }

    /// <summary>Two contexts load the same Sent invoice; one cancels, the other records a payment; they save in the given order.</summary>
    private async Task<(Guid InvoiceId, bool CancelSaved, bool PaymentSaved)> RaceCancelAgainstPaymentAsync(bool cancelSavesFirst)
    {
        var (invoice, _) = await AddSentAsync();
        await using var canceller = database.CreateContext();
        await using var payer = database.CreateContext();
        var cancelStore = new EfInvoiceStore(canceller);
        var payStore = new EfInvoiceStore(payer);
        var cancelCopy = (await cancelStore.FindByIdAsync(invoice.Id, default))!;
        var payCopy = (await payStore.FindByIdAsync(invoice.Id, default))!;

        Assert.True(cancelCopy.Cancel(Now.AddMinutes(1)));
        payCopy.RecordPayment(TodaySaudi(), 100.00m, PaymentMethod.Cash, null, Now.AddMinutes(2));

        bool cancelSaved, paymentSaved;
        if (cancelSavesFirst)
        {
            cancelSaved = await cancelStore.SaveChangesAsync(default);
            paymentSaved = await payStore.SaveChangesAsync(default);
        }
        else
        {
            paymentSaved = await payStore.SaveChangesAsync(default);
            cancelSaved = await cancelStore.SaveChangesAsync(default);
        }

        return (invoice.Id, cancelSaved, paymentSaved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_payment_racing_a_cancel_of_the_same_Sent_invoice_has_exactly_one_winner_in_either_order(bool cancelSavesFirst)
    {
        var (invoiceId, cancelSaved, paymentSaved) = await RaceCancelAgainstPaymentAsync(cancelSavesFirst);

        // The one that saved first wins, the other is refused (the row version), and the invoice is never both.
        Assert.Equal(cancelSavesFirst, cancelSaved);
        Assert.Equal(!cancelSavesFirst, paymentSaved);
        var loaded = await ReloadAsync(invoiceId);
        Assert.False(loaded.Status == InvoiceStatus.Cancelled && loaded.Payments.Count > 0);
        Assert.Equal(cancelSavesFirst ? InvoiceStatus.Cancelled : InvoiceStatus.Sent, loaded.Status);
        Assert.Equal(cancelSavesFirst ? 0 : 1, loaded.Payments.Count);
    }

    [Fact]
    public async Task Cancelling_a_Sent_invoice_that_has_a_saved_payment_is_refused_by_the_real_service_and_the_row_stays_Sent()
    {
        var (invoice, _) = await AddSentAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).RecordPaymentAsync(invoice.Id, Pay(100.00m), default)).IsSuccess);
        }

        await using (var context = database.CreateContext())
        {
            var result = await NewService(context).CancelAsync(invoice.Id, default);
            Assert.True(result.IsFailure);
            Assert.Equal("invoice_has_payments", result.Error!.Code);
        }

        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Sent, loaded.Status);
        Assert.Null(loaded.CancelledAtUtc);
        Assert.Empty(loaded.AllowedTransitions);
    }

    [Fact]
    public async Task Cancelling_a_Sent_invoice_with_no_payment_still_works_on_real_SQL()
    {
        var (invoice, _) = await AddSentAsync();

        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).CancelAsync(invoice.Id, default)).IsSuccess);
        }

        Assert.Equal(InvoiceStatus.Cancelled, (await ReloadAsync(invoice.Id)).Status);
    }

    [Fact]
    public async Task A_payment_cannot_be_added_to_a_Paid_or_a_Cancelled_invoice_through_the_real_service()
    {
        var (paid, _) = await AddSentAsync();
        var (cancelled, _) = await AddSentAsync();
        await using (var context = database.CreateContext())
        {
            var service = NewService(context);
            Assert.True((await service.RecordPaymentAsync(paid.Id, Pay(230.00m), default)).IsSuccess);
            Assert.True((await service.CancelAsync(cancelled.Id, default)).IsSuccess);
        }

        await using (var context = database.CreateContext())
        {
            var service = NewService(context);
            var onPaid = await service.RecordPaymentAsync(paid.Id, Pay(1.00m), default);
            var onCancelled = await service.RecordPaymentAsync(cancelled.Id, Pay(1.00m), default);

            Assert.Equal("invoice_payment_not_allowed", onPaid.Error!.Code);
            Assert.Equal("invoice_payment_not_allowed", onCancelled.Error!.Code);
        }

        Assert.Equal(1, await PaymentRowsAsync(paid.Id));
        Assert.Equal(0, await PaymentRowsAsync(cancelled.Id));
    }

    // ---- amounts do not move ----

    [Fact]
    public async Task A_repriced_product_in_the_database_leaves_what_remains_as_read_back_from_SQL()
    {
        var (invoice, product) = await AddSentAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).RecordPaymentAsync(invoice.Id, Pay(100.00m), default)).IsSuccess);
        }

        await using (var writer = database.CreateContext())
        {
            await writer.Database.ExecuteSqlRawAsync("UPDATE Products SET UnitPrice = 999.99, VatPercent = 0 WHERE Id = {0}", product.Id);
        }

        var loaded = await ReloadAsync(invoice.Id);
        Assert.Equal(230.00m, loaded.Amounts.GrandTotal);
        Assert.Equal(130.00m, loaded.Remaining);
    }

    // ---- the database's own checks (raw rows, past the application) ----

    private async Task<SqlException> InsertRawPaymentAsync(Guid invoiceId, string amount, string method)
    {
        await using var context = database.CreateContext();
        return await Assert.ThrowsAsync<SqlException>(async () => await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Payments (Id, InvoiceId, PaidOn, Amount, Method, Note, RecordedAtUtc) " +
            $"VALUES (NEWID(), {{0}}, '2030-06-01', {amount}, '{method}', NULL, SYSUTCDATETIME())",
            invoiceId));
    }

    [Theory]
    [InlineData("0", "Cash")]
    [InlineData("-1.00", "Cash")]
    [InlineData("10000000000000.00", "Cash")]
    [InlineData("10.00", "Card")]
    [InlineData("10.00", "cheque")]
    public async Task The_database_refuses_a_zero_negative_or_too_large_amount_and_an_unknown_method(string amount, string method)
    {
        var (invoice, _) = await AddSentAsync();

        var error = await InsertRawPaymentAsync(invoice.Id, amount, method);

        Assert.Contains("CK_Payments_", error.Message);
    }

    [Fact]
    public async Task The_database_accepts_the_three_methods_and_the_largest_allowed_amount()
    {
        var (invoice, _) = await AddSentAsync();
        await using var context = database.CreateContext();

        foreach (var method in new[] { "Cash", "Bank", "Other" })
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Payments (Id, InvoiceId, PaidOn, Amount, Method, Note, RecordedAtUtc) " +
                $"VALUES (NEWID(), {{0}}, '2030-06-01', 9999999999999.99, '{method}', NULL, SYSUTCDATETIME())",
                invoice.Id);
        }

        Assert.Equal(3, await PaymentRowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_payment_cannot_point_at_an_invoice_that_does_not_exist_and_an_invoice_with_payments_cannot_be_deleted()
    {
        var (invoice, _) = await AddSentAsync();
        await using (var context = database.CreateContext())
        {
            Assert.True((await NewService(context).RecordPaymentAsync(invoice.Id, Pay(10.00m), default)).IsSuccess);
        }

        var orphan = await InsertRawPaymentAsync(Guid.NewGuid(), "10.00", "Cash");
        await using var deleter = database.CreateContext();
        var delete = await Assert.ThrowsAsync<SqlException>(async () =>
            await deleter.Database.ExecuteSqlRawAsync("DELETE FROM Invoices WHERE Id = {0}", invoice.Id));

        Assert.Contains("FK_Payments_Invoices_InvoiceId", orphan.Message);
        Assert.Contains("FK_Payments_Invoices_InvoiceId", delete.Message);
        Assert.Equal(1, await PaymentRowsAsync(invoice.Id));
    }
}
