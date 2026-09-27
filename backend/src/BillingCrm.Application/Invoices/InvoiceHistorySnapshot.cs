using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>One line of an invoice as it was saved at a moment: enough to say what changed, nothing looked up again.</summary>
public sealed record InvoiceHistoryLine(Guid Id, string ProductName, string Sku, decimal Quantity, decimal UnitPrice, decimal VatPercent);

/// <summary>
/// What an editable invoice looked like at one moment (spec 010): its header and its lines with the values each one saved. The service takes one
/// before an edit and one after, and <see cref="InvoiceHistoryBuilder.DraftEdited"/> says what differs. The customer's name is passed in (it is
/// not part of the invoice) and is the name at that moment.
/// </summary>
public sealed record InvoiceHistorySnapshot(
    Guid InvoiceId,
    Guid CustomerId,
    string CustomerName,
    DateOnly DueDate,
    string? Notes,
    decimal Discount,
    IReadOnlyList<InvoiceHistoryLine> Lines)
{
    public static InvoiceHistorySnapshot From(Invoice invoice, string customerName) => new(
        invoice.Id,
        invoice.CustomerId,
        customerName,
        invoice.DueDate,
        invoice.Notes,
        invoice.Discount,
        invoice.Lines
            .Select(line => new InvoiceHistoryLine(line.Id, line.ProductName, line.ProductSku, line.Quantity, line.UnitPrice, line.VatPercent))
            .ToList());
}
