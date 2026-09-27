using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// Spec 012, User Story 2: a genuine SQL Server deadlock on an invoice save (not merely a lost optimistic-concurrency race) is answered
/// the same way any other lost race already is — never an unhandled exception.
///
/// Forcing a real deadlock needs two saves to hold overlapping locks in opposite order at the same instant, which two independent,
/// fast <c>SaveChangesAsync</c> calls do not reliably do on their own (one usually finishes before the other really starts contending,
/// which only exercises the row-version check, already correct). This test instead opens an **explicit** transaction per context so a
/// row's lock is held across two of that context's own <c>SaveChangesAsync</c> calls, with a short delay between them: context A locks
/// invoice 1 then (after the delay) asks for invoice 2's lock; context B locks invoice 2 then asks for invoice 1's — the classic
/// opposite-order recipe, guaranteed to overlap because of the delay, so the engine's own deadlock monitor (a few seconds, by default)
/// is guaranteed to pick a victim. This is slower than most tests in this suite by design.
/// </summary>
public class EfInvoiceStoreDeadlockTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task<(Invoice First, Invoice Second)> SeedTwoSentInvoicesAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        }

        var product = Product.Create("Consulting hour", $"P-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), ProductUnit.Hour, 100m, 15m, Now);
        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        }

        var first = Invoice.Create(customer.Id, DateOnly.FromDateTime(Now.AddDays(30)), null, Now);
        first.AddLine(product, 1m, Now);
        Assert.True(first.Send(Now));
        var second = Invoice.Create(customer.Id, DateOnly.FromDateTime(Now.AddDays(30)), null, Now);
        second.AddLine(product, 1m, Now);
        Assert.True(second.Send(Now));
        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            await store.AddAsync(first, default);
            await store.AddAsync(second, default);
        }

        return (first, second);
    }

    /// <summary>Locks <paramref name="lockFirst"/>'s row, waits, then tries to also touch <paramref name="lockSecond"/>'s — through
    /// the real <see cref="EfInvoiceStore.SaveChangesAsync"/>, twice, inside one explicit transaction so the first lock is still held
    /// when the second save asks for the other row.</summary>
    private async Task<bool> CancelBothWithinOneTransactionAsync(Guid lockFirst, Guid lockSecond, TimeSpan delayBeforeSecond)
    {
        await using var context = database.CreateContext();
        var store = new EfInvoiceStore(context);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var first = (await store.FindByIdAsync(lockFirst, default))!;
        Assert.True(first.Cancel(Now.AddMinutes(1)));
        if (!await store.SaveChangesAsync(default))
        {
            return false;
        }

        await Task.Delay(delayBeforeSecond);

        var second = (await store.FindByIdAsync(lockSecond, default))!;
        Assert.True(second.Cancel(Now.AddMinutes(1)));
        var saved = await store.SaveChangesAsync(default);
        if (saved)
        {
            await transaction.CommitAsync();
        }

        return saved;
    }

    [Fact]
    public async Task A_genuine_deadlock_between_two_saves_leaves_one_winner_and_the_loser_gets_false_never_an_exception()
    {
        var (first, second) = await SeedTwoSentInvoicesAsync();
        var delay = TimeSpan.FromMilliseconds(300);

        // A locks `first` then (after the delay) wants `second`; B locks `second` then (after the same delay) wants `first`: opposite
        // order, overlapping in time, so the engine must deadlock one of them.
        var taskA = CancelBothWithinOneTransactionAsync(first.Id, second.Id, delay);
        var taskB = CancelBothWithinOneTransactionAsync(second.Id, first.Id, delay);

        var results = await Task.WhenAll(taskA, taskB); // must never throw: SaveChangesAsync itself must catch the deadlock

        Assert.Equal(1, results.Count(saved => saved));
        Assert.Equal(1, results.Count(saved => !saved));

        await using var check = database.CreateContext();
        var store = new EfInvoiceStore(check);
        var cancelledCount = new[] { (await store.FindByIdAsync(first.Id, default))!, (await store.FindByIdAsync(second.Id, default))! }
            .Count(invoice => invoice.Status == InvoiceStatus.Cancelled);
        Assert.Equal(2, cancelledCount); // the winner's own two-invoice cancel went through whole; the loser's transaction rolled back entirely
    }
}
