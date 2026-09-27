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
/// Spec 012, User Story 5: payments have been described as permanent since spec 008 (no edit, void or delete route has ever existed),
/// closed below the application layer the same way spec 010 closed it for invoice history: on real SQL Server no update and no delete
/// of a payment row ever succeeds, however it is attempted, while recording one (an insert) still works.
/// </summary>
public class EfPaymentImmutabilityTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private const string Refusal = "Payments cannot be changed or deleted.";
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Actor Agent = new("agent1@crm.local", StaffRole.Agent);

    private static DateOnly TodaySaudi() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private static string Due => TodaySaudi().AddDays(30).ToString("yyyy-MM-dd");

    private InvoiceService NewService(BillingDbContext context) =>
        new(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser { Current = Agent });

    private static InvoiceDto Ok(Result<InvoiceDto> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    /// <summary>A Sent invoice with one recorded (but not settling) payment, made through the real service.</summary>
    private async Task<InvoiceDto> CreateSentInvoiceWithPaymentAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
            Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        }

        InvoiceDto invoice;
        await using (var context = database.CreateContext())
        {
            invoice = Ok(await NewService(context).CreateAsync(
                new InvoiceInput(customer.Id.ToString(), Due, null, [new InvoiceLineInput(null, product.Id.ToString(), 3m)]), default));
        }

        await using (var context = database.CreateContext())
        {
            Ok(await NewService(context).SendAsync(invoice.Id, default));
        }

        await using (var context = database.CreateContext())
        {
            var pay = new PaymentInput(TodaySaudi().ToString("yyyy-MM-dd"), 50m, "Cash", "First part payment");
            invoice = Ok(await NewService(context).RecordPaymentAsync(invoice.Id, pay, default));
        }

        return invoice;
    }

    /// <summary>Every column of every payment of one invoice as one text per row, oldest first: any change to any byte shows.</summary>
    private async Task<List<string>> RowsAsync(Guid? invoiceId = null)
    {
        await using var context = database.CreateContext();
        var where = invoiceId is null ? string.Empty : $"WHERE InvoiceId = '{invoiceId}'";
        return await context.Database.SqlQueryRaw<string>(
                "SELECT CONCAT(Id, '|', InvoiceId, '|', CONVERT(varchar(10), PaidOn, 23), '|', Amount, '|', Method, '|', " +
                "ISNULL(Note, ''), '|', CONVERT(varchar(30), RecordedAtUtc, 126)) AS [Value] " +
                $"FROM Payments {where} ORDER BY RecordedAtUtc")
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
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync();
        Assert.NotEmpty(before);

        await AssertRawRefusedAsync("UPDATE Payments SET Note = 'x'");

        Assert.Equal(before, await RowsAsync());
        Assert.NotEmpty(await RowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_raw_update_of_one_row_by_id_is_refused_and_the_row_is_unchanged()
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync(invoice.Id);
        var id = before[0].Split('|')[0];

        await AssertRawRefusedAsync($"UPDATE Payments SET Note = 'x' WHERE Id = '{id}'");

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    [Theory]
    [InlineData("Amount = 1")]
    [InlineData("Method = 'Bank'")]
    [InlineData("Note = 'changed'")]
    [InlineData("PaidOn = '2020-01-01'")]
    [InlineData("RecordedAtUtc = SYSUTCDATETIME()")]
    [InlineData("InvoiceId = NEWID()")]
    public async Task A_raw_update_of_any_column_is_refused(string assignment)
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync(invoice.Id);

        await AssertRawRefusedAsync($"UPDATE Payments SET {assignment} WHERE InvoiceId = '{invoice.Id}'");

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    // ---- delete is refused ----

    [Fact]
    public async Task A_raw_delete_of_every_row_of_one_row_and_of_one_invoices_rows_is_refused_and_the_rows_remain()
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync();
        var id = (await RowsAsync(invoice.Id))[0].Split('|')[0];

        await AssertRawRefusedAsync("DELETE FROM Payments");
        await AssertRawRefusedAsync($"DELETE FROM Payments WHERE Id = '{id}'");
        await AssertRawRefusedAsync($"DELETE FROM Payments WHERE InvoiceId = '{invoice.Id}'");

        Assert.Equal(before, await RowsAsync());
    }

    [Fact]
    public async Task A_statement_that_matches_no_row_is_refused_too_because_the_rule_does_not_depend_on_what_matches()
    {
        // An INSTEAD OF trigger runs once per statement, even when no row matches, so the refusal is unconditional.
        await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync();

        await AssertRawRefusedAsync("DELETE FROM Payments WHERE 1 = 0");
        await AssertRawRefusedAsync("UPDATE Payments SET Amount = Amount WHERE 1 = 0");

        Assert.Equal(before, await RowsAsync());
    }

    // ---- through EF ----

    [Fact]
    public async Task ExecuteUpdate_and_ExecuteDelete_through_EF_fail_and_change_nothing()
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var update = await Assert.ThrowsAnyAsync<Exception>(() =>
                context.Payments.Where(payment => payment.InvoiceId == invoice.Id).ExecuteUpdateAsync(set => set.SetProperty(payment => payment.Note, "x")));
            AssertRefused(update);
        }

        await using (var context = database.CreateContext())
        {
            var delete = await Assert.ThrowsAnyAsync<Exception>(() =>
                context.Payments.Where(payment => payment.InvoiceId == invoice.Id).ExecuteDeleteAsync());
            AssertRefused(delete);
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_tracked_payment_edited_and_saved_fails_and_changes_nothing()
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var payment = await context.Payments.FirstAsync(candidate => candidate.InvoiceId == invoice.Id);
            context.Entry(payment).Property(candidate => candidate.Note).CurrentValue = "changed";

            var error = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            AssertRefused(error);
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    [Fact]
    public async Task A_tracked_payment_removed_and_saved_fails_and_the_row_remains()
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = await RowsAsync(invoice.Id);

        await using (var context = database.CreateContext())
        {
            var payment = await context.Payments.FirstAsync(candidate => candidate.InvoiceId == invoice.Id);
            context.Payments.Remove(payment);

            var error = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            AssertRefused(error);
        }

        Assert.Equal(before, await RowsAsync(invoice.Id));
    }

    // ---- insert still works ----

    [Fact]
    public async Task Recording_a_payment_through_the_invoice_still_works()
    {
        var invoice = await CreateSentInvoiceWithPaymentAsync();
        var before = (await RowsAsync(invoice.Id)).Count;

        await using (var context = database.CreateContext())
        {
            var pay = new PaymentInput(TodaySaudi().ToString("yyyy-MM-dd"), 50m, "Bank", "Second part payment");
            Ok(await NewService(context).RecordPaymentAsync(invoice.Id, pay, default));
        }

        Assert.Equal(before + 1, (await RowsAsync(invoice.Id)).Count);
    }
}
