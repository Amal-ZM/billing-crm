namespace BillingCrm.Domain;

/// <summary>
/// One thing that changed in a history entry (spec 010): its name, its value before (null when it did not exist: a Created entry, an added
/// line) and its value after (null when it was removed or cleared). Every value is <b>text as it was at the time</b>: names as they were then,
/// dates as <c>yyyy-MM-dd</c>, money as exact two-decimal text (<c>100.00</c>), statuses by their stored name. Nothing here is ever recalculated
/// or looked up again, so a later change to a customer, a product or a user cannot change what an entry says.
/// </summary>
public sealed record HistoryChange(string Field, string? Before, string? After);
