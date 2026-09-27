using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// In-memory store for HTTP-level tests. It hands out increasing numbers like the database sequence, keeps the same
/// invoice object between requests (so a saved edit is what the next read sees), and takes customer names from the
/// in-memory customer store the same factory uses. Real ordering, paging and number behaviour are proven in the
/// Infrastructure tests.
/// </summary>
public sealed class InMemoryInvoiceStore : IInvoiceStore
{
    private readonly object _gate = new();
    private readonly List<Invoice> _invoices = [];
    private int _nextNumber = 1;

    /// <summary>Set by the factory to the in-memory customer store, for the customer names in list rows.</summary>
    public ICustomerStore? Customers { get; set; }

    public IReadOnlyList<Invoice> All
    {
        get
        {
            lock (_gate)
            {
                return _invoices.ToList();
            }
        }
    }

    public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_invoices.FirstOrDefault(invoice => invoice.Id == id));
        }
    }

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            typeof(Invoice).GetProperty(nameof(Invoice.Number))!.SetValue(invoice, _nextNumber++);
            _invoices.Add(invoice);
            CommitPendingHistory();
            return Task.CompletedTask;
        }
    }

    /// <summary>When true, an edit reports that someone else changed the invoice first (a concurrent edit).</summary>
    public bool SaveLosesRace { get; set; }

    public Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (SaveLosesRace)
            {
                _pendingHistory.Clear();
                return Task.FromResult(false);
            }

            CommitPendingHistory();
            return Task.FromResult(true);
        }
    }

    // ---- history (spec 010): pending until a save succeeds, then committed; a lost race discards them ----

    private readonly List<InvoiceHistoryEntry> _pendingHistory = [];
    private readonly List<InvoiceHistoryEntry> _committedHistory = [];

    /// <summary>The entries that were saved for one invoice, in the order they were written (oldest first).</summary>
    public IReadOnlyList<InvoiceHistoryEntry> HistoryOf(Guid invoiceId)
    {
        lock (_gate)
        {
            return _committedHistory.Where(entry => entry.InvoiceId == invoiceId).ToList();
        }
    }

    public void RecordHistory(InvoiceHistoryEntry entry)
    {
        lock (_gate)
        {
            _pendingHistory.Add(entry);
        }
    }

    public Task<IReadOnlyList<InvoiceHistoryEntry>> ListHistoryAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InvoiceHistoryEntry>>(HistoryOf(invoiceId).Reverse().ToList());

    public Task<int?> FindNumberAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_invoices.FirstOrDefault(invoice => invoice.Id == invoiceId) is { } invoice ? (int?)invoice.Number : null);
        }
    }

    // Callers hold the gate.
    private void CommitPendingHistory()
    {
        _committedHistory.AddRange(_pendingHistory);
        _pendingHistory.Clear();
    }

    public async Task<PagedResult<InvoiceListRow>> SearchAsync(InvoiceSearchFilter filter, DateOnly today, int page, int pageSize, CancellationToken cancellationToken)
    {
        List<Invoice> ordered;
        lock (_gate)
        {
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

            ordered = matching.OrderByDescending(invoice => invoice.CreatedAtUtc).ThenByDescending(invoice => invoice.Number).ToList();
        }

        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = new List<InvoiceListRow>();
        foreach (var invoice in ordered.Skip(offset).Take(pageSize))
        {
            var customer = Customers is null ? null : await Customers.FindByIdAsync(invoice.CustomerId, cancellationToken);
            rows.Add(new InvoiceListRow(
                invoice.Id,
                invoice.Number,
                invoice.Status,
                invoice.CustomerId,
                customer?.Name ?? "Customer",
                invoice.DueDate,
                invoice.CreatedAtUtc));
        }

        return PagedResult<InvoiceListRow>.Create(rows, page, pageSize, ordered.Count);
    }

    // ---- the Home dashboard's five figures (spec 011) ----

    public Task<DashboardSummaryDto> GetSummaryAsync(DateOnly today, CancellationToken cancellationToken)
    {
        lock (_gate)
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
    }

    /// <summary>The payment that made a Paid invoice Paid: its own last-recorded payment, whatever its date (no more payments can follow).</summary>
    private static DateOnly SettlingPaymentDate(Invoice invoice) =>
        invoice.Payments.OrderByDescending(payment => payment.RecordedAtUtc).First().PaidOn;

    private static bool IsSameMonth(DateOnly date, DateOnly today) => date.Year == today.Year && date.Month == today.Month;
}
