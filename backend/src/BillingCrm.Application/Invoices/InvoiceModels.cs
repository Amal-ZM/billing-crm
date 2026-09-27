using System.Globalization;
using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>
/// One line of the body of add and edit. A line is either <b>existing</b> (<c>Id</c> given: only its quantity can change) or
/// <b>new</b> (<c>ProductId</c> given: copied from the product now). Ids are text so a malformed one becomes a named field
/// error; the quantity binds to <c>decimal?</c> so a typed 1.50 never passes through a floating-point number.
/// </summary>
public sealed record InvoiceLineInput(string? Id, string? ProductId, decimal? Quantity);

/// <summary>
/// The body of add and edit (one shape for both). The due date is text (<c>yyyy-MM-dd</c>), parsed by the validator.
/// <c>Discount</c> is the optional header discount in SAR: absent, <c>null</c> and 0 all mean none. Because an edit describes the
/// <b>whole</b> Draft, leaving it out clears a saved discount. It binds to <c>decimal?</c> so a typed 20.10 never passes through a
/// floating-point number.
/// </summary>
public sealed record InvoiceInput(
    string? CustomerId,
    string? DueDate,
    string? Notes,
    IReadOnlyList<InvoiceLineInput>? Lines,
    decimal? Discount = null);

/// <summary>
/// The body of a payment (spec 008). The date is text (<c>yyyy-MM-dd</c>), parsed by the validator; the amount binds to
/// <c>decimal?</c> so a typed 100.10 never passes through a floating-point number; the method is text so a wrong one becomes a named
/// field error; the note is optional.
/// </summary>
public sealed record PaymentInput(string? Date, decimal? Amount, string? Method, string? Note);

/// <summary>A payment as saved: permanent, so it is only ever shown, never edited.</summary>
public sealed record PaymentDto(Guid Id, string Date, decimal Amount, string Method, string? Note, DateTime RecordedAtUtc)
{
    public static PaymentDto From(Payment payment) => new(
        payment.Id,
        InvoiceDto.FormatDate(payment.PaidOn),
        payment.Amount,
        payment.Method.ToString(),
        payment.Note,
        payment.RecordedAtUtc);
}

/// <summary>
/// The list query exactly as it arrives in the query string; parsed and validated by the service. <c>Status</c>, <c>Overdue</c> and
/// <c>PaidMonth</c> exist only so a Home dashboard card's link can reach its own matching set (spec 011); the invoice list screen has no
/// filter control of its own. <c>Overdue</c>/<c>PaidMonth</c> other than exactly <c>"true"</c> behave as absent; they matter only paired
/// with the matching <c>Status</c> (<c>Sent</c> for <c>Overdue</c>, <c>Paid</c> for <c>PaidMonth</c>) and are silently ignored otherwise.
/// </summary>
public sealed record InvoiceQuery(string? Page, string? PageSize, string? Status = null, string? Overdue = null, string? PaidMonth = null);

/// <summary>
/// The invoice list's narrowing, already parsed and validated (spec 011): <c>Status</c> alone, or combined with <c>Overdue</c> (only
/// meaningful with <see cref="Domain.InvoiceStatus.Sent"/>) or <c>PaidThisMonth</c> (only meaningful with
/// <see cref="Domain.InvoiceStatus.Paid"/>). <c>null</c> means every status, exactly as before this spec.
/// </summary>
public sealed record InvoiceSearchFilter(InvoiceStatus? Status, bool Overdue, bool PaidThisMonth)
{
    /// <summary>No filter at all: every invoice, exactly as the list read before this spec.</summary>
    public static readonly InvoiceSearchFilter None = new(null, false, false);
}

/// <summary>
/// The Home dashboard's five figures (spec 011): computed fresh from the current invoices and payments on every read, never cached,
/// precomputed or stored. <see cref="UnpaidCount"/> counts every Sent invoice, on-time and overdue alike; <see cref="OverdueCount"/> is the
/// subset of those whose due date has passed. <see cref="PaidThisMonthCount"/> counts a Paid invoice by its own settling payment's date
/// (its last-recorded payment), never an earlier part payment's. <see cref="UnpaidAmount"/> is the SAR sum, over every Sent invoice, of
/// what it still owes (<see cref="Invoice.Remaining"/>). A Cancelled invoice is never part of any of the five figures.
/// </summary>
public sealed record DashboardSummaryDto(int DraftCount, int UnpaidCount, int OverdueCount, int PaidThisMonthCount, decimal UnpaidAmount);

public sealed record InvoiceCustomerDto(Guid Id, string Name, bool IsActive);

/// <summary>
/// A line as saved: the product's values are the copy taken when the line was added. <c>Net</c>, <c>DiscountShare</c> and
/// <c>Vat</c> are calculated from that copy (see <see cref="InvoiceMoney"/>), in SAR with two decimals; while the invoice is a Draft they are calculated, afterwards they are the frozen values.
/// </summary>
public sealed record InvoiceLineDto(
    Guid Id,
    int Position,
    Guid ProductId,
    string ProductName,
    string Sku,
    string Unit,
    decimal UnitPrice,
    decimal VatPercent,
    decimal Quantity,
    decimal Net,
    decimal DiscountShare,
    decimal Vat)
{
    public static InvoiceLineDto From(InvoiceLine line, LineAmounts amounts) => new(
        line.Id,
        line.Position,
        line.ProductId,
        line.ProductName,
        line.ProductSku,
        line.Unit.ToString(),
        line.UnitPrice,
        line.VatPercent,
        line.Quantity,
        amounts.Net,
        amounts.DiscountShare,
        amounts.Vat);
}

/// <summary>What the invoice comes to, in SAR with two decimals: <c>GrandTotal = NetAfterDiscount + TotalVat</c>.</summary>
public sealed record InvoiceTotalsDto(decimal TotalNet, decimal Discount, decimal NetAfterDiscount, decimal TotalVat, decimal GrandTotal)
{
    public static InvoiceTotalsDto From(InvoiceAmounts amounts) => new(
        amounts.TotalNet,
        amounts.Discount,
        amounts.NetAfterDiscount,
        amounts.TotalVat,
        amounts.GrandTotal);
}

public sealed record InvoiceDto(
    Guid Id,
    string Number,
    string Status,
    InvoiceCustomerDto Customer,
    string DueDate,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsOverdue,
    DateTime? SentAtUtc,
    DateTime? CancelledAtUtc,
    IReadOnlyList<string> AllowedTransitions,
    bool IsEditable,
    decimal Discount,
    InvoiceTotalsDto Totals,
    IReadOnlyList<InvoiceLineDto> Lines,
    decimal Paid,
    decimal Remaining,
    bool AcceptsPayment,
    IReadOnlyList<PaymentDto> Payments)
{
    /// <param name="today">Today's date in Saudi Arabia: a Sent invoice past its due date is shown as overdue (never stored).</param>
    public static InvoiceDto From(Invoice invoice, Customer customer, DateOnly today)
    {
        // Calculated once, from the lines as saved and the discount; the lines come back in the same order as their amounts.
        var amounts = invoice.Amounts;
        var lines = invoice.Lines;
        var balance = InvoiceMoney.Balance(amounts.GrandTotal, invoice.Payments.Select(payment => payment.Amount));

        return new InvoiceDto(
            invoice.Id,
            InvoiceNumber.Format(invoice.Number),
            invoice.Status.ToString(),
            new InvoiceCustomerDto(customer.Id, customer.Name, customer.IsActive),
            FormatDate(invoice.DueDate),
            invoice.Notes,
            invoice.CreatedAtUtc,
            invoice.UpdatedAtUtc,
            invoice.IsOverdue(today),
            invoice.SentAtUtc,
            invoice.CancelledAtUtc,
            invoice.AllowedTransitions.Select(status => status.ToString()).ToList(),
            invoice.IsEditable,
            amounts.Discount,
            InvoiceTotalsDto.From(amounts),
            lines.Select((line, index) => InvoiceLineDto.From(line, amounts.Lines[index])).ToList(),
            balance.Paid,
            balance.Remaining,
            invoice.AcceptsPayment,
            invoice.Payments.Select(PaymentDto.From).ToList());
    }

    internal static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>What the list shows: the number as text, the customer's current name, no lines and no amounts.</summary>
public sealed record InvoiceSummary(
    Guid Id,
    string Number,
    string Status,
    Guid CustomerId,
    string CustomerName,
    string DueDate,
    DateTime CreatedAtUtc,
    bool IsOverdue);

/// <summary>What the store returns for one list row, before the number and date are turned into text.</summary>
public sealed record InvoiceListRow(
    Guid Id,
    int Number,
    InvoiceStatus Status,
    Guid CustomerId,
    string CustomerName,
    DateOnly DueDate,
    DateTime CreatedAtUtc)
{
    /// <param name="today">Today's date in Saudi Arabia, to work out whether a Sent invoice is overdue.</param>
    public InvoiceSummary ToSummary(DateOnly today) => new(
        Id,
        InvoiceNumber.Format(Number),
        Status.ToString(),
        CustomerId,
        CustomerName,
        InvoiceDto.FormatDate(DueDate),
        CreatedAtUtc,
        Invoice.IsOverdue(Status, DueDate, today));
}

/// <summary>One thing that changed in a history entry, exactly as recorded: text as it was at the time, <c>null</c> for "did not exist" or "removed".</summary>
public sealed record HistoryChangeDto(string Field, string? Before, string? After)
{
    public static HistoryChangeDto From(HistoryChange change) => new(change.Field, change.Before, change.After);
}

/// <summary>
/// One history entry as the browser reads it (spec 010): the kind and role by name, the time in UTC, the user's email and role as they were then.
/// It carries no invoice id and no user id: only the entry's own id and the text copied at the time.
/// </summary>
public sealed record HistoryEntryDto(
    Guid Id,
    string Kind,
    DateTime OccurredAtUtc,
    string ActorEmail,
    string ActorRole,
    IReadOnlyList<HistoryChangeDto> Changes)
{
    public static HistoryEntryDto From(InvoiceHistoryEntry entry) => new(
        entry.Id,
        entry.Kind.ToString(),
        entry.OccurredAtUtc,
        entry.ActorEmail,
        entry.ActorRole.ToString(),
        entry.Changes.Select(HistoryChangeDto.From).ToList());
}

/// <summary>
/// An invoice's history: its number as text, the entries newest first, and whether the history starts with the invoice's creation. An invoice made
/// before history was recorded has no <c>Created</c> entry, so <c>StartsWithCreation</c> is false and the browser says so; it never works this out.
/// </summary>
public sealed record InvoiceHistoryDto(string Number, bool StartsWithCreation, IReadOnlyList<HistoryEntryDto> Entries);
