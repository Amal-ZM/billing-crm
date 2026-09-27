using System.Globalization;
using BillingCrm.Application.Auth;
using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>
/// Says what each kind of change to an invoice looked like (spec 010): pure, no stores, no clock, no user lookup. Every value is <b>text as it is at
/// that moment</b>, in the invariant culture whatever the server's language: money and quantities and percentages with two decimals, dates as
/// <c>yyyy-MM-dd</c>, statuses by their stored name. Nothing here calculates money (the figures are the ones the invoice already holds) and nothing is
/// looked up again later, so a later change to a customer, a product or a user can never change what an entry says.
/// </summary>
public static class InvoiceHistoryBuilder
{
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A discount of zero means "no discount", so it is recorded as nothing.</summary>
    private static string? DiscountText(decimal discount) => discount > 0m ? Money(discount) : null;

    private static string LineName(string productName, string sku) => $"{productName} ({sku})";

    private static string LineDescription(InvoiceHistoryLine line) =>
        $"{Quantity(line.Quantity)} x SAR {Money(line.UnitPrice)}, VAT {Percent(line.VatPercent)}%";

    /// <summary>The invoice was first saved: what it started with (customer, due date, notes when present, discount when above zero and each line).</summary>
    public static InvoiceHistoryEntry Created(Invoice invoice, string customerName, Actor actor, DateTime nowUtc)
    {
        var changes = new List<HistoryChange>
        {
            new("Customer", null, customerName),
            new("Due date", null, Date(invoice.DueDate)),
        };

        if (!string.IsNullOrEmpty(invoice.Notes))
        {
            changes.Add(new HistoryChange("Notes", null, invoice.Notes));
        }

        if (DiscountText(invoice.Discount) is { } discount)
        {
            changes.Add(new HistoryChange("Discount (SAR)", null, discount));
        }

        foreach (var line in invoice.Lines)
        {
            changes.Add(new HistoryChange(
                $"Line: {LineName(line.ProductName, line.ProductSku)}",
                null,
                $"{Quantity(line.Quantity)} x SAR {Money(line.UnitPrice)}, VAT {Percent(line.VatPercent)}%"));
        }

        return InvoiceHistoryEntry.Create(invoice.Id, HistoryKind.Created, nowUtc, actor.Email, actor.Role, changes);
    }

    /// <summary>
    /// A Draft was saved: only what differs between the two snapshots, never what stayed the same. Lines are matched by id (two lines of one product
    /// stay apart). Returns <c>null</c> when nothing differs, so an edit that changes nothing leaves no entry.
    /// </summary>
    public static InvoiceHistoryEntry? DraftEdited(InvoiceHistorySnapshot before, InvoiceHistorySnapshot after, Actor actor, DateTime nowUtc)
    {
        var changes = new List<HistoryChange>();

        if (before.CustomerId != after.CustomerId)
        {
            changes.Add(new HistoryChange("Customer", before.CustomerName, after.CustomerName));
        }

        if (before.DueDate != after.DueDate)
        {
            changes.Add(new HistoryChange("Due date", Date(before.DueDate), Date(after.DueDate)));
        }

        var notesBefore = string.IsNullOrEmpty(before.Notes) ? null : before.Notes;
        var notesAfter = string.IsNullOrEmpty(after.Notes) ? null : after.Notes;
        if (!string.Equals(notesBefore, notesAfter, StringComparison.Ordinal))
        {
            changes.Add(new HistoryChange("Notes", notesBefore, notesAfter));
        }

        if (before.Discount != after.Discount)
        {
            changes.Add(new HistoryChange("Discount (SAR)", DiscountText(before.Discount), DiscountText(after.Discount)));
        }

        var beforeById = before.Lines.ToDictionary(line => line.Id);
        var afterIds = after.Lines.Select(line => line.Id).ToHashSet();

        // In the order of the saved lines: a changed quantity, or a new line.
        foreach (var line in after.Lines)
        {
            if (!beforeById.TryGetValue(line.Id, out var old))
            {
                changes.Add(new HistoryChange($"Line added: {LineName(line.ProductName, line.Sku)}", null, LineDescription(line)));
            }
            else if (old.Quantity != line.Quantity)
            {
                changes.Add(new HistoryChange($"Line quantity: {LineName(line.ProductName, line.Sku)}", Quantity(old.Quantity), Quantity(line.Quantity)));
            }
        }

        foreach (var line in before.Lines.Where(line => !afterIds.Contains(line.Id)))
        {
            changes.Add(new HistoryChange($"Line removed: {LineName(line.ProductName, line.Sku)}", LineDescription(line), null));
        }

        return changes.Count == 0
            ? null
            : InvoiceHistoryEntry.Create(after.InvoiceId, HistoryKind.DraftEdited, nowUtc, actor.Email, actor.Role, changes);
    }

    /// <summary>Draft to Sent.</summary>
    public static InvoiceHistoryEntry Sent(Guid invoiceId, Actor actor, DateTime nowUtc) =>
        InvoiceHistoryEntry.Create(
            invoiceId,
            HistoryKind.Sent,
            nowUtc,
            actor.Email,
            actor.Role,
            [new HistoryChange("Status", InvoiceStatus.Draft.ToString(), InvoiceStatus.Sent.ToString())]);

    /// <summary>
    /// A payment was recorded: what it was and what the invoice owed before and after (figures the invoice already holds). The payment that settles
    /// the invoice is this same entry and also shows Sent to Paid; there is no separate entry for the status.
    /// </summary>
    public static InvoiceHistoryEntry PaymentRecorded(
        Payment payment,
        decimal paidBefore,
        decimal remainingBefore,
        decimal paidAfter,
        decimal remainingAfter,
        bool settled,
        Actor actor,
        DateTime nowUtc)
    {
        var changes = new List<HistoryChange>
        {
            new("Payment date", null, Date(payment.PaidOn)),
            new("Payment amount (SAR)", null, Money(payment.Amount)),
            new("Payment method", null, payment.Method.ToString()),
        };

        if (!string.IsNullOrEmpty(payment.Note))
        {
            changes.Add(new HistoryChange("Payment note", null, payment.Note));
        }

        changes.Add(new HistoryChange("Amount paid (SAR)", Money(paidBefore), Money(paidAfter)));
        changes.Add(new HistoryChange("Amount remaining (SAR)", Money(remainingBefore), Money(remainingAfter)));

        if (settled)
        {
            changes.Add(new HistoryChange("Status", InvoiceStatus.Sent.ToString(), InvoiceStatus.Paid.ToString()));
        }

        return InvoiceHistoryEntry.Create(payment.InvoiceId, HistoryKind.Payment, nowUtc, actor.Email, actor.Role, changes);
    }

    /// <summary>Cancelled from Draft or Sent. <paramref name="from"/> is the <b>stored</b> status (Overdue is never an event).</summary>
    public static InvoiceHistoryEntry Cancelled(Guid invoiceId, InvoiceStatus from, Actor actor, DateTime nowUtc) =>
        InvoiceHistoryEntry.Create(
            invoiceId,
            HistoryKind.Cancelled,
            nowUtc,
            actor.Email,
            actor.Role,
            [new HistoryChange("Status", from.ToString(), InvoiceStatus.Cancelled.ToString())]);
}
