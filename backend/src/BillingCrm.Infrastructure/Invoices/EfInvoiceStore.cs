using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Invoices;

public sealed class EfInvoiceStore(BillingDbContext context) : IInvoiceStore
{
    // Tracked on purpose: an edit changes the invoice, its lines and its payments through the aggregate and then calls SaveChangesAsync.
    // Two child collections are loaded, so it is a split query: one joined query would multiply lines by payments.
    public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Invoices
            .Include(invoice => invoice.Lines)
            .Include(invoice => invoice.Payments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        // The invoice and its lines are inserted in one transaction. The number is not set here: the database draws it
        // from its sequence when the row is inserted, and EF reads it back into invoice.Number.
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // The row version is checked only when the invoice row itself is written. A change to a line alone (for example a quantity
        // edited with the same changed-time the invoice already had) would leave the invoice row untouched and skip the check, so
        // an invoice whose lines are being written is always written too. The same holds for a payment (spec 008): two payments
        // loaded at the same balance must not both be saved, and a payment must not slip in beside a cancel.
        var tracker = context.ChangeTracker;
        var changedLineInvoiceIds = tracker.Entries<InvoiceLine>()
            .Where(line => line.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(line => line.Entity.InvoiceId)
            .Concat(tracker.Entries<Payment>()
                .Where(payment => payment.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(payment => payment.Entity.InvoiceId))
            .ToHashSet();
        foreach (var invoice in tracker.Entries<Invoice>().Where(entry => entry.State == EntityState.Unchanged && changedLineInvoiceIds.Contains(entry.Entity.Id)))
        {
            invoice.Property(candidate => candidate.UpdatedAtUtc).IsModified = true;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed this invoice first, for example removed a line this save was going to change or delete.
            // That is an expected outcome of two people editing at once, not a bug: report it and let the caller say so. The
            // whole save is rolled back, so nothing of it was written.
            return false;
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            // A SQL Server deadlock victim (spec 012): from the caller's point of view this is the same "someone else's change
            // interfered" outcome as a lost optimistic-concurrency race above, so it gets the same answer (false -> 409), not an
            // unhandled exception. No retry: the caller's own request, tried again, is the retry (the same thing a person does when
            // told an invoice was changed by someone else). EF Core does not consistently wrap this one in a DbUpdateException (unlike
            // a concurrency conflict), so the raw SqlException is matched too, wherever in the chain it appears.
            return false;
        }
    }

    /// <summary>True for a SQL Server deadlock (error 1205): the caller was chosen as the victim, not because of anything wrong with
    /// its own request. Anything else (a constraint violation, a genuine data problem, any other <see cref="DbUpdateException"/>) still
    /// propagates, whether it is the exception itself or nested inside one.</summary>
    private static bool IsDeadlock(Exception ex) =>
        ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 } || ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException { Number: 1205 };

    // Only adds to the change tracker. The entry is written by the SaveChanges of SaveChangesAsync or AddAsync, in the same transaction as the
    // change it describes, so it exists only if that change was saved (a lost race rolls both back). Nothing updates or removes an entry.
    public void RecordHistory(InvoiceHistoryEntry entry) => context.InvoiceHistoryEntries.Add(entry);

    public async Task<IReadOnlyList<InvoiceHistoryEntry>> ListHistoryAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await context.InvoiceHistoryEntries
            .AsNoTracking()
            .Where(entry => entry.InvoiceId == invoiceId)
            .OrderByDescending(entry => entry.Sequence)
            .ToListAsync(cancellationToken);

    public Task<int?> FindNumberAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Id == invoiceId)
            .Select(invoice => (int?)invoice.Number)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<InvoiceListRow>> SearchAsync(InvoiceSearchFilter filter, DateOnly today, int page, int pageSize, CancellationToken cancellationToken)
    {
        var invoices = context.Invoices.AsNoTracking().AsQueryable();
        if (filter.Status is { } status)
        {
            invoices = invoices.Where(invoice => invoice.Status == status);
        }

        if (filter.Overdue)
        {
            invoices = invoices.Where(invoice => invoice.DueDate < today);
        }

        if (filter.PaidThisMonth)
        {
            // Each Paid invoice's own settling payment (its last-recorded one): read as a separate step (the same pattern
            // GetSummaryAsync uses), then narrow by id, rather than a subquery inside this WHERE.
            var settledThisMonth = await context.Invoices.AsNoTracking()
                .Where(invoice => invoice.Status == InvoiceStatus.Paid)
                .Select(invoice => new
                {
                    invoice.Id,
                    PaidOn = context.Payments
                        .Where(payment => payment.InvoiceId == invoice.Id)
                        .OrderByDescending(payment => payment.RecordedAtUtc)
                        .Select(payment => payment.PaidOn)
                        .First(),
                })
                .Where(candidate => candidate.PaidOn.Year == today.Year && candidate.PaidOn.Month == today.Month)
                .Select(candidate => candidate.Id)
                .ToListAsync(cancellationToken);
            invoices = invoices.Where(invoice => settledThisMonth.Contains(invoice.Id));
        }

        var totalCount = await invoices.CountAsync(cancellationToken);

        // Long arithmetic: a huge page number must not wrap around into a negative or arbitrary offset.
        // A page past the last one is an empty page with the real total, without asking the database for rows.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
        {
            return PagedResult<InvoiceListRow>.Create([], page, pageSize, totalCount);
        }

        // Newest first: created time, then number, so the order is total and pages never overlap or skip. The customer's
        // CURRENT name is joined in (the list shows names, not saved copies). No lines are read.
        var rows = await (
                from invoice in invoices
                join customer in context.Customers.AsNoTracking() on invoice.CustomerId equals customer.Id
                orderby invoice.CreatedAtUtc descending, invoice.Number descending
                select new InvoiceListRow(
                    invoice.Id,
                    invoice.Number,
                    invoice.Status,
                    invoice.CustomerId,
                    customer.Name,
                    invoice.DueDate,
                    invoice.CreatedAtUtc))
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<InvoiceListRow>.Create(rows, page, pageSize, totalCount);
    }

    // The Home dashboard's five figures (spec 011): five independent, read-only queries, computed fresh every call, never cached. Each
    // reads context.Payments directly (a correlated subquery), the same way EfPaymentTests already does, rather than through the
    // Invoice.Payments navigation.
    public async Task<DashboardSummaryDto> GetSummaryAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var draftCount = await context.Invoices.AsNoTracking().CountAsync(invoice => invoice.Status == InvoiceStatus.Draft, cancellationToken);
        var unpaidCount = await context.Invoices.AsNoTracking().CountAsync(invoice => invoice.Status == InvoiceStatus.Sent, cancellationToken);
        var overdueCount = await context.Invoices.AsNoTracking()
            .CountAsync(invoice => invoice.Status == InvoiceStatus.Sent && invoice.DueDate < today, cancellationToken);

        // Each Paid invoice's own settling payment: its last-recorded payment (no more can follow once it is Paid), whatever its own
        // PaidOn date; that date alone decides "this month", never an earlier part payment's.
        var paidThisMonthCount = await context.Invoices.AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.Paid)
            .Select(invoice => context.Payments
                .Where(payment => payment.InvoiceId == invoice.Id)
                .OrderByDescending(payment => payment.RecordedAtUtc)
                .Select(payment => payment.PaidOn)
                .First())
            .Where(paidOn => paidOn.Year == today.Year && paidOn.Month == today.Month)
            .CountAsync(cancellationToken);

        // SQL Server refuses SUM(x - SUM(y)) as one aggregate over another in the same query, so each Sent invoice's own remaining
        // balance is read as its own row first (still one correlated subquery per row, translated fine on its own) and summed in .NET.
        var remainings = await context.Invoices.AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.Sent)
            .Select(invoice => invoice.FrozenGrandTotal!.Value - context.Payments
                .Where(payment => payment.InvoiceId == invoice.Id)
                .Sum(payment => payment.Amount))
            .ToListAsync(cancellationToken);
        var unpaidAmount = remainings.Sum();

        return new DashboardSummaryDto(draftCount, unpaidCount, overdueCount, paidThisMonthCount, unpaidAmount);
    }
}
