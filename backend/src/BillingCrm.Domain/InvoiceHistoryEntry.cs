using System.Collections.ObjectModel;

namespace BillingCrm.Domain;

/// <summary>
/// One permanent record of one change to one invoice (spec 010): what kind of event it was, when, who made it (their email and role <b>as they
/// were then</b>) and what changed, each with its value before and after. It is made once, by the invoice service, in the same save as the
/// change it describes, and it is <b>never changed or removed</b>: there are no setters and no methods here, nothing in the application updates
/// or deletes one, and the database refuses to. It is not part of the <see cref="Invoice"/> aggregate (it only grows and is only read), so it
/// is not loaded with the invoice.
/// </summary>
public class InvoiceHistoryEntry
{
    // For EF Core.
    private InvoiceHistoryEntry()
    {
        ActorEmail = string.Empty;
        Changes = new ReadOnlyCollection<HistoryChange>([]);
    }

    private InvoiceHistoryEntry(Guid invoiceId, HistoryKind kind, DateTime occurredAtUtc, string actorEmail, StaffRole actorRole, IReadOnlyList<HistoryChange> changes)
    {
        Id = Guid.NewGuid();
        InvoiceId = invoiceId;
        Kind = kind;
        OccurredAtUtc = occurredAtUtc;
        ActorEmail = actorEmail;
        ActorRole = actorRole;
        Changes = new ReadOnlyCollection<HistoryChange>(changes.ToList());
    }

    public Guid Id { get; private set; }

    /// <summary>0 until the database gives it a value when the entry is saved: the order in which entries were applied (higher is later).</summary>
    public long Sequence { get; private set; }

    public Guid InvoiceId { get; private set; }

    public HistoryKind Kind { get; private set; }

    /// <summary>When the change was made (UTC): the same instant the invoice's own changed time gets.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>The signed-in user's email address as it was when they made the change; it never follows a later change to the account.</summary>
    public string ActorEmail { get; private set; }

    /// <summary>The signed-in user's role as it was when they made the change.</summary>
    public StaffRole ActorRole { get; private set; }

    /// <summary>What changed, in order. A read-only copy: nothing can add to it or alter it.</summary>
    public IReadOnlyList<HistoryChange> Changes { get; private set; }

    /// <summary>
    /// Makes an entry. An entry without a user, without a real kind or role, or without any change cannot exist, so a bug that tried to make
    /// one fails here instead of writing a meaningless record.
    /// </summary>
    /// <exception cref="ArgumentException">The email is empty, the kind or the role is not a defined value, or there is no change.</exception>
    public static InvoiceHistoryEntry Create(
        Guid invoiceId,
        HistoryKind kind,
        DateTime occurredAtUtc,
        string actorEmail,
        StaffRole actorRole,
        IReadOnlyList<HistoryChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (string.IsNullOrWhiteSpace(actorEmail))
        {
            throw new ArgumentException("An entry needs the user who made the change.", nameof(actorEmail));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Unknown kind of history entry.", nameof(kind));
        }

        if (!Enum.IsDefined(actorRole))
        {
            throw new ArgumentException("Unknown role.", nameof(actorRole));
        }

        if (changes.Count == 0)
        {
            throw new ArgumentException("An entry must record at least one change.", nameof(changes));
        }

        return new InvoiceHistoryEntry(invoiceId, kind, occurredAtUtc, actorEmail.Trim(), actorRole, changes);
    }
}
