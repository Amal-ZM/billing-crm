namespace BillingCrm.Domain;

/// <summary>
/// Where an invoice is in its life. Only these four are ever stored. <b>Overdue is not a status</b>: it is how a
/// <see cref="Sent"/> invoice is shown when its due date is before today (see <see cref="Invoice.IsOverdue"/>), so it needs no
/// job and can never be out of date. The legal changes between statuses are in <see cref="InvoiceStatusRules"/> and nowhere else.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>A working copy: the only status that can be edited.</summary>
    Draft,

    /// <summary>Issued to the customer: read-only, with its amounts frozen.</summary>
    Sent,

    /// <summary>Settled (spec 008): a payment that brings the remaining amount to exactly zero moves a Sent invoice here. Nobody requests it and it is final.</summary>
    Paid,

    /// <summary>Withdrawn. Final: never sent, edited or reinstated, and still on record with its number and amounts.</summary>
    Cancelled,
}
