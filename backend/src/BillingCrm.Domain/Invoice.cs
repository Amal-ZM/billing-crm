namespace BillingCrm.Domain;

/// <summary>
/// A bill for one customer, with its lines. It is created as a <see cref="InvoiceStatus.Draft"/> and can be edited only while it
/// is a Draft (spec 005). Spec 007 lets it leave Draft (<see cref="Send"/>, <see cref="Cancel"/>, the legal changes in
/// <see cref="InvoiceStatusRules"/>); from then on it is read-only and its amounts are frozen. The invoice owns its lines: they are
/// added, changed and removed through this class so every change is one unit. Spec 008 adds <see cref="Payments"/>: money received
/// against a Sent invoice, recorded only through <see cref="RecordPayment"/>, permanent, with the balance (<see cref="Paid"/>,
/// <see cref="Remaining"/>) always worked out from them and the payment that settles the invoice making it Paid in the same call.
/// There is no delete.
/// </summary>
public class Invoice
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<Payment> _payments = [];

    // For EF Core.
    private Invoice()
    {
    }

    private Invoice(Guid customerId, DateOnly dueDate, string? notes, decimal discount, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        Status = InvoiceStatus.Draft;
        DueDate = dueDate;
        Notes = notes;
        Discount = discount;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>0 until the database draws the next number from its sequence when the invoice is first saved.</summary>
    public int Number { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>The stored status. Overdue is never stored: see <see cref="IsOverdue"/>.</summary>
    public InvoiceStatus Status { get; private set; }

    public DateOnly DueDate { get; private set; }

    /// <summary>Trimmed by the validator; null means no notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// The header discount in SAR (0 means none), taken off the total net before VAT. The service checks it is from 0 to the total
    /// net. It is locked with everything else once the invoice leaves Draft.
    /// </summary>
    public decimal Discount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the invoice was sent; null while it is a Draft and for an invoice cancelled straight from Draft.</summary>
    public DateTime? SentAtUtc { get; private set; }

    /// <summary>When the invoice was cancelled; null otherwise.</summary>
    public DateTime? CancelledAtUtc { get; private set; }

    // The amounts as they were when the invoice left Draft (all null while it is a Draft). The discount is Discount, locked from then on.
    public decimal? FrozenTotalNet { get; private set; }

    public decimal? FrozenNetAfterDiscount { get; private set; }

    public decimal? FrozenTotalVat { get; private set; }

    public decimal? FrozenGrandTotal { get; private set; }

    /// <summary>
    /// The database's row version, an opaque token that changes on every save. Two changes to the same invoice in the same instant
    /// cannot both be saved: the second is refused as "changed by someone else". Nothing outside persistence reads it.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>True only while the invoice is a Draft: every other status is read-only.</summary>
    public bool IsEditable => Status == InvoiceStatus.Draft;

    /// <summary>
    /// Overdue is how a <b>Sent</b> invoice is shown when its due date is before <paramref name="today"/> (the calendar date in Saudi
    /// Arabia). It is worked out, never stored, so it needs no job and cannot be stale; on the due date itself it is not yet overdue,
    /// and a Draft, Paid or Cancelled invoice is never overdue.
    /// </summary>
    public bool IsOverdue(DateOnly today) => IsOverdue(Status, DueDate, today);

    /// <summary>
    /// The same rule for something that is not a whole invoice (a list row has only its status and due date), so the rule is written
    /// once: Sent, and the due date is before today.
    /// </summary>
    public static bool IsOverdue(InvoiceStatus status, DateOnly dueDate, DateOnly today) => status == InvoiceStatus.Sent && dueDate < today;

    /// <summary>The lines in the order they were added.</summary>
    public IReadOnlyList<InvoiceLine> Lines => _lines.OrderBy(line => line.Position).ThenBy(line => line.Id).ToList();

    /// <summary>The payments in the order they were recorded (the id makes the order total).</summary>
    public IReadOnlyList<Payment> Payments => _payments.OrderBy(payment => payment.RecordedAtUtc).ThenBy(payment => payment.Id).ToList();

    /// <summary>The sum of the payments, SAR with two decimals. Worked out each time, never stored (see <see cref="InvoiceMoney.Balance"/>).</summary>
    public decimal Paid => Balance().Paid;

    /// <summary>
    /// The grand total minus what has been paid. For an invoice that has left Draft the grand total is the frozen one, so a product
    /// changing later cannot move what is owed. Never stored.
    /// </summary>
    public decimal Remaining => Balance().Remaining;

    /// <summary>True only when the invoice is Sent (also when it is shown as Overdue) and something remains to be paid.</summary>
    public bool AcceptsPayment => Status == InvoiceStatus.Sent && Remaining > 0m;

    /// <summary>
    /// The changes a person could ask for, before their role: what the status table permits, <b>without Paid</b> (only a settling payment
    /// causes it, nobody requests it) and <b>without Cancelled once there is a payment</b> (there are no refunds, so an invoice
    /// holding money is not cancelled).
    /// </summary>
    public IReadOnlyList<InvoiceStatus> AllowedTransitions => InvoiceStatusRules.NextStatuses(Status)
        .Where(next => next != InvoiceStatus.Paid && !(next == InvoiceStatus.Cancelled && _payments.Count > 0))
        .ToList();

    private InvoiceBalance Balance() => InvoiceMoney.Balance(Amounts.GrandTotal, _payments.Select(payment => payment.Amount));

    /// <summary>
    /// What the invoice comes to. A <b>Draft</b> calculates it now from each line's saved quantity, unit price and VAT percent (never
    /// from the product they were copied from) and the discount (see <see cref="InvoiceMoney"/>). An invoice that has left Draft returns
    /// the numbers that were frozen at that moment, so nothing later can move them.
    /// </summary>
    /// <remarks>
    /// Reading must never fail. The service refuses a discount above the net, but two people saving <b>different rows</b> of the
    /// same Draft at once can leave a saved discount above the net that neither save saw (the row version now stops that); such a
    /// discount is shown as the whole net instead of throwing. If a non-Draft invoice somehow lacks a frozen value, the amounts are
    /// calculated instead of failing. (Ties in the discount sharing follow the line order, made total by the id.)
    /// </remarks>
    public InvoiceAmounts Amounts => (Status != InvoiceStatus.Draft ? Frozen() : null) ?? Calculate();

    /// <remarks>Callers validate first (InvoiceValidator) and check the customer is active; a new invoice is always a Draft.</remarks>
    public static Invoice Create(Guid customerId, DateOnly dueDate, string? notes, DateTime nowUtc, decimal discount = 0m) =>
        new(customerId, dueDate, notes, discount, nowUtc);

    /// <summary>
    /// Draft to Sent. Returns false, changing nothing, when the invoice is not a Draft. Otherwise it records the time, freezes the
    /// amounts and moves <see cref="UpdatedAtUtc"/>; nothing else about the invoice changes.
    /// </summary>
    public bool Send(DateTime nowUtc)
    {
        if (!InvoiceStatusRules.CanChange(Status, InvoiceStatus.Sent))
        {
            return false;
        }

        FreezeAmounts();
        Status = InvoiceStatus.Sent;
        SentAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// Draft or Sent (also Overdue) to Cancelled. Returns false, changing nothing, when the invoice is Paid or already Cancelled, or
    /// when it has a payment (spec 008: there are no refunds, so an invoice holding money is not cancelled).
    /// Cancelling a Draft freezes its amounts at that moment; a Sent invoice keeps the amounts frozen when it was sent. Who may
    /// cancel (an Admin) is decided at the API, not here.
    /// </summary>
    public bool Cancel(DateTime nowUtc)
    {
        if (!InvoiceStatusRules.CanChange(Status, InvoiceStatus.Cancelled) || _payments.Count > 0)
        {
            return false;
        }

        if (Status == InvoiceStatus.Draft)
        {
            FreezeAmounts();
        }

        Status = InvoiceStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// Records money received against a <b>Sent</b> invoice (also one shown as Overdue). It adds the payment and moves
    /// <see cref="UpdatedAtUtc"/>; when the remaining amount is then exactly zero it makes the invoice <b>Paid</b> in this same call,
    /// so a payment and the status it causes are never saved apart. The caller (the service) has already checked everything a person
    /// can get wrong and reports it as an error; this guards the aggregate itself, so a payment on an invoice that is not Sent, a
    /// zero or negative amount, more than two decimals, or more than what remains (overpaying) throws and changes nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The invoice is not Sent, or the amount is not from 0.01 to the remaining amount.</exception>
    public Payment RecordPayment(DateOnly paidOn, decimal amount, PaymentMethod method, string? note, DateTime nowUtc)
    {
        if (Status != InvoiceStatus.Sent)
        {
            throw new InvalidOperationException("Only a Sent invoice can take a payment.");
        }

        if (amount <= 0m || amount != decimal.Round(amount, 2) || amount > Remaining)
        {
            throw new InvalidOperationException("A payment must be more than zero, have at most two decimals and not exceed the remaining amount.");
        }

        var payment = Payment.Create(Id, paidOn, amount, method, note, nowUtc);
        _payments.Add(payment);
        UpdatedAtUtc = nowUtc;

        if (Remaining == 0m && InvoiceStatusRules.CanChange(Status, InvoiceStatus.Paid))
        {
            Status = InvoiceStatus.Paid;
        }

        return payment;
    }

    /// <summary>Changes the customer, due date, notes or discount. Returns true, and moves <see cref="UpdatedAtUtc"/>, only if something changed.</summary>
    /// <exception cref="InvalidOperationException">The invoice is not a Draft. The service refuses that with a conflict first, so reaching this is a bug.</exception>
    public bool UpdateDetails(Guid customerId, DateOnly dueDate, string? notes, decimal discount, DateTime nowUtc)
    {
        EnsureDraft();
        if (CustomerId == customerId && DueDate == dueDate && Notes == notes && Discount == discount)
        {
            return false;
        }

        CustomerId = customerId;
        DueDate = dueDate;
        Notes = notes;
        Discount = discount;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// Adds a line for the product, copying its name, SKU, unit, unit price and VAT percent as they are now. The new line goes
    /// after the highest position that remains, so lines always keep the order they were added in (a position freed by removing
    /// the last line can be taken again by a new one; the position index is not unique for that reason).
    /// </summary>
    /// <exception cref="InvalidOperationException">The invoice is not a Draft.</exception>
    public InvoiceLine AddLine(Product product, decimal quantity, DateTime nowUtc)
    {
        EnsureDraft();
        var position = _lines.Count == 0 ? 1 : _lines.Max(line => line.Position) + 1;
        var line = InvoiceLine.CreateFrom(product, position, quantity);
        line.AttachTo(Id);
        _lines.Add(line);
        UpdatedAtUtc = nowUtc;
        return line;
    }

    /// <summary>Returns true, and moves <see cref="UpdatedAtUtc"/>, only if the line was there.</summary>
    /// <exception cref="InvalidOperationException">The invoice is not a Draft.</exception>
    public bool RemoveLine(Guid lineId, DateTime nowUtc)
    {
        EnsureDraft();
        var line = _lines.FirstOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return false;
        }

        _lines.Remove(line);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Returns true, and moves <see cref="UpdatedAtUtc"/>, only if the line exists and its quantity really changed.</summary>
    /// <exception cref="InvalidOperationException">The invoice is not a Draft.</exception>
    public bool SetLineQuantity(Guid lineId, decimal quantity, DateTime nowUtc)
    {
        EnsureDraft();
        var line = _lines.FirstOrDefault(candidate => candidate.Id == lineId);
        if (line is null || !line.SetQuantity(quantity))
        {
            return false;
        }

        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>The aggregate protects itself: a Sent, Paid or Cancelled invoice is read-only, whoever asks.</summary>
    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only a Draft invoice can be edited.");
        }
    }

    /// <summary>The 006 calculation, with its never-throws clamp of a discount above the net.</summary>
    private InvoiceAmounts Calculate()
    {
        var lines = Lines.Select(line => new AmountLine(line.Quantity, line.UnitPrice, line.VatPercent)).ToList();
        var totalNet = InvoiceMoney.Calculate(lines, 0m).TotalNet;
        return InvoiceMoney.Calculate(lines, Math.Clamp(Discount, 0m, totalNet));
    }

    /// <summary>The frozen numbers, or null if any is missing (then the caller calculates instead of failing).</summary>
    private InvoiceAmounts? Frozen()
    {
        var lines = Lines;
        if (FrozenTotalNet is not { } totalNet
            || FrozenNetAfterDiscount is not { } netAfterDiscount
            || FrozenTotalVat is not { } totalVat
            || FrozenGrandTotal is not { } grandTotal
            || lines.Any(line => line.FrozenNet is null || line.FrozenDiscountShare is null || line.FrozenVat is null))
        {
            return null;
        }

        return new InvoiceAmounts(
            lines.Select(line => new LineAmounts(line.FrozenNet!.Value, line.FrozenDiscountShare!.Value, line.FrozenVat!.Value)).ToList(),
            totalNet,
            Discount,
            netAfterDiscount,
            totalVat,
            grandTotal);
    }

    /// <summary>Writes down the amounts as they are now (the invoice is still a Draft, so they are calculated) into the invoice and every line.</summary>
    private void FreezeAmounts()
    {
        var amounts = Calculate();

        // The discount that was actually applied (a legacy Draft could hold one above its net, which the calculation clamps): keep the
        // stored discount equal to it, so a frozen invoice always shows total net - discount = net after discount.
        Discount = amounts.Discount;
        FrozenTotalNet = amounts.TotalNet;
        FrozenNetAfterDiscount = amounts.NetAfterDiscount;
        FrozenTotalVat = amounts.TotalVat;
        FrozenGrandTotal = amounts.GrandTotal;

        var lines = Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Freeze(amounts.Lines[i]);
        }
    }
}
