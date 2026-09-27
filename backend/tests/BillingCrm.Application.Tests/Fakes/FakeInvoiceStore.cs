using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests.Fakes;

/// <summary>
/// In-memory store for service tests. Like the database it hands out increasing numbers when an invoice is added, and it
/// counts the saves so a test can prove that a refused or unchanged edit wrote nothing.
/// </summary>
public sealed class FakeInvoiceStore : IInvoiceStore
{
    private readonly List<Invoice> _invoices = [];
    private int _nextNumber = 1;

    /// <summary>Customer names for the list rows (the real store joins the Customers table).</summary>
    public Dictionary<Guid, string> CustomerNames { get; } = [];

    public int AddCalls { get; private set; }

    public int SaveChangesCalls { get; private set; }

    /// <summary>When true, <see cref="SaveChangesAsync"/> reports that someone else changed the invoice first (a concurrent edit).</summary>
    public bool SaveLosesRace { get; set; }

    public (int Page, int PageSize)? LastSearch { get; private set; }

    public IReadOnlyList<Invoice> All => _invoices;

    /// <summary>Puts an invoice in the store as if it had been saved (with the next number).</summary>
    public void Seed(Invoice invoice)
    {
        AssignNumber(invoice, _nextNumber++);
        _invoices.Add(invoice);
    }

    public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.FirstOrDefault(invoice => invoice.Id == id));

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        AddCalls++;
        AssignNumber(invoice, _nextNumber++);
        _invoices.Add(invoice);
        CommitPendingHistory();
        return Task.CompletedTask;
    }

    public Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        if (SaveLosesRace)
        {
            // All or nothing, like the database: a save that loses discards the entries recorded for it.
            _pendingHistory.Clear();
            return Task.FromResult(false);
        }

        CommitPendingHistory();
        return Task.FromResult(true);
    }

    // ---- history (spec 010): entries are pending until a save succeeds, then committed; a lost race discards them ----

    private readonly List<InvoiceHistoryEntry> _pendingHistory = [];
    private readonly List<InvoiceHistoryEntry> _committedHistory = [];

    /// <summary>Entries recorded but not yet saved.</summary>
    public int PendingHistoryCount => _pendingHistory.Count;

    /// <summary>The entries that were saved for one invoice, in the order they were written (oldest first).</summary>
    public IReadOnlyList<InvoiceHistoryEntry> HistoryOf(Guid invoiceId) =>
        _committedHistory.Where(entry => entry.InvoiceId == invoiceId).ToList();

    public void RecordHistory(InvoiceHistoryEntry entry) => _pendingHistory.Add(entry);

    public Task<IReadOnlyList<InvoiceHistoryEntry>> ListHistoryAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InvoiceHistoryEntry>>(HistoryOf(invoiceId).Reverse().ToList());

    public Task<int?> FindNumberAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.FirstOrDefault(invoice => invoice.Id == invoiceId) is { } invoice ? (int?)invoice.Number : null);

    private void CommitPendingHistory()
    {
        _committedHistory.AddRange(_pendingHistory);
        _pendingHistory.Clear();
    }

    public Task<PagedResult<InvoiceListRow>> SearchAsync(InvoiceSearchFilter filter, DateOnly today, int page, int pageSize, CancellationToken cancellationToken)
    {
        LastSearch = (page, pageSize);
        var matching = _invoices.AsEnumerable();
        if (filter.Status is { } status)
        {
            matching = matching.Where(invoice => invoice.Status == status);
        }

        if (filter.Overdue)
        {
            matching = matching.Where(invoice => invoice.DueDate < today);
        }

        if (filter.PaidThisMonth)
        {
            matching = matching.Where(invoice => IsSameMonth(SettlingPaymentDate(invoice), today));
        }

        var ordered = matching.OrderByDescending(invoice => invoice.CreatedAtUtc).ThenByDescending(invoice => invoice.Number).ToList();
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = ordered
            .Skip(offset)
            .Take(pageSize)
            .Select(invoice => new InvoiceListRow(
                invoice.Id,
                invoice.Number,
                invoice.Status,
                invoice.CustomerId,
                CustomerNames.GetValueOrDefault(invoice.CustomerId, "Customer"),
                invoice.DueDate,
                invoice.CreatedAtUtc))
            .ToList();
        return Task.FromResult(PagedResult<InvoiceListRow>.Create(rows, page, pageSize, ordered.Count));
    }

    /// <summary>The number has a private setter (only the database sets it in production); a test double sets it the same way EF does.</summary>
    private static void AssignNumber(Invoice invoice, int number) =>
        typeof(Invoice).GetProperty(nameof(Invoice.Number))!.SetValue(invoice, number);

    // ---- the Home dashboard's five figures (spec 011) ----

    public Task<DashboardSummaryDto> GetSummaryAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var sent = _invoices.Where(invoice => invoice.Status == InvoiceStatus.Sent).ToList();
        var paidThisMonth = _invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Paid)
            .Count(invoice => IsSameMonth(SettlingPaymentDate(invoice), today));

        return Task.FromResult(new DashboardSummaryDto(
            _invoices.Count(invoice => invoice.Status == InvoiceStatus.Draft),
            sent.Count,
            sent.Count(invoice => invoice.DueDate < today),
            paidThisMonth,
            sent.Sum(invoice => invoice.Remaining)));
    }

    /// <summary>The payment that made a Paid invoice Paid: its own last-recorded payment, whatever its date (no more payments can follow).</summary>
    private static DateOnly SettlingPaymentDate(Invoice invoice) =>
        invoice.Payments.OrderByDescending(payment => payment.RecordedAtUtc).First().PaidOn;

    private static bool IsSameMonth(DateOnly date, DateOnly today) => date.Year == today.Year && date.Month == today.Month;
}
