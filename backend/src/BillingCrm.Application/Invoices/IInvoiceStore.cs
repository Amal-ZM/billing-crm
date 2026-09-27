using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>
/// There is no <c>Try*</c> here (unlike customers and products): no value a person types can violate a unique index,
/// because the invoice number is drawn by the database itself, never chosen.
/// </summary>
public interface IInvoiceStore
{
    /// <summary>The invoice <b>with its lines</b>, tracked so an edit can be saved with <see cref="SaveChangesAsync"/>.</summary>
    Task<Invoice?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Saves a new invoice and its lines in one step. The database gives it its number, which is filled in.</summary>
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the changes made to an invoice returned by <see cref="FindByIdAsync"/> (added, changed and removed lines too), all or
    /// nothing. Returns false, without throwing, only when someone else changed the same invoice first in a way that makes this
    /// save impossible (for example they removed a line this save was going to change); other failures throw.
    /// </summary>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Newest first (created time, then number). Each row carries the customer's current name. <paramref name="filter"/> narrows the
    /// result to one of the Home dashboard's views (spec 011); <see cref="InvoiceSearchFilter.None"/> is every invoice, exactly as before
    /// this spec. <paramref name="today"/> is the caller's own notion of today, used only when the filter needs it (Overdue, this month).
    /// </summary>
    Task<PagedResult<InvoiceListRow>> SearchAsync(InvoiceSearchFilter filter, DateOnly today, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// The Home dashboard's five figures (spec 011), computed fresh from the current invoices and payments: never cached, precomputed
    /// or stored, and this read changes nothing. <paramref name="today"/> is the caller's own notion of today (the same fixed Saudi date
    /// used everywhere else), used to decide Overdue and "this month".
    /// </summary>
    Task<DashboardSummaryDto> GetSummaryAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a history entry (spec 010) to the current unit of work. It is <b>not saved by itself</b>: the next <see cref="SaveChangesAsync"/>
    /// or <see cref="AddAsync"/> writes it in the same transaction as the change it describes, so an entry exists only if the change was
    /// saved, and a save that is refused or loses a race writes no entry. There is no member that updates or removes an entry.
    /// </summary>
    void RecordHistory(InvoiceHistoryEntry entry);

    /// <summary>One invoice's history entries, newest first (highest sequence first), not tracked. Empty when it has none or the id is unknown.</summary>
    Task<IReadOnlyList<InvoiceHistoryEntry>> ListHistoryAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>The invoice's number, or null when there is no such invoice. Reads nothing else, so it costs no lines and no payments.</summary>
    Task<int?> FindNumberAsync(Guid invoiceId, CancellationToken cancellationToken);
}
