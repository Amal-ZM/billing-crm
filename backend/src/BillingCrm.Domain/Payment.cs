namespace BillingCrm.Domain;

/// <summary>
/// A record that money was received for one invoice (spec 008): the date it was received, the amount in SAR, how it was paid and an
/// optional note, plus the time it was recorded. It belongs to its invoice and is made only by <see cref="Invoice.RecordPayment"/>.
/// It is <b>permanent</b>: nothing edits, voids or deletes a payment, so there are no setters and no methods here. Like a line, it
/// is saved and read through the invoice. This is a record staff make after receiving money, not a payment gateway.
/// </summary>
public class Payment
{
    // For EF Core.
    private Payment()
    {
    }

    private Payment(Guid invoiceId, DateOnly paidOn, decimal amount, PaymentMethod method, string? note, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        InvoiceId = invoiceId;
        PaidOn = paidOn;
        Amount = amount;
        Method = method;
        Note = note;
        RecordedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public Guid InvoiceId { get; private set; }

    /// <summary>The calendar date the money was received (a date with no time of day).</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>SAR, exact decimal with two places, greater than zero. Never rounded: the validator refuses a third decimal place.</summary>
    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>Trimmed by the validator; null means no note.</summary>
    public string? Note { get; private set; }

    /// <summary>When the payment was recorded (UTC), separate from the date it was received.</summary>
    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>Only the invoice makes payments, so its rules (Sent, not above the remaining) cannot be skipped.</summary>
    internal static Payment Create(Guid invoiceId, DateOnly paidOn, decimal amount, PaymentMethod method, string? note, DateTime nowUtc) =>
        new(invoiceId, paidOn, amount, method, note, nowUtc);
}
