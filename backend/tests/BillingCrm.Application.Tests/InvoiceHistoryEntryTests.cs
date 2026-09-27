using System.Reflection;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The invoice history entry (spec 010): a permanent record with no way to change it once made.</summary>
public class InvoiceHistoryEntryTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid InvoiceId = Guid.NewGuid();

    private static readonly IReadOnlyList<HistoryChange> Changes = [new("Status", "Draft", "Sent")];

    private static InvoiceHistoryEntry Entry(
        HistoryKind kind = HistoryKind.Sent,
        string email = "agent1@crm.local",
        StaffRole role = StaffRole.Agent,
        IReadOnlyList<HistoryChange>? changes = null) =>
        InvoiceHistoryEntry.Create(InvoiceId, kind, Now, email, role, changes ?? Changes);

    // ---- the kinds ----

    [Fact]
    public void There_are_exactly_five_kinds_and_no_others()
    {
        Assert.Equal(
            ["Cancelled", "Created", "DraftEdited", "Payment", "Sent"],
            Enum.GetNames<HistoryKind>().OrderBy(name => name, StringComparer.Ordinal));
    }

    // ---- creating one ----

    [Fact]
    public void An_entry_keeps_everything_it_was_made_with()
    {
        var entry = Entry(HistoryKind.Payment, "admin@crm.local", StaffRole.Admin, [new("Payment amount (SAR)", null, "100.00"), new("Status", "Sent", "Paid")]);

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(InvoiceId, entry.InvoiceId);
        Assert.Equal(HistoryKind.Payment, entry.Kind);
        Assert.Equal(Now, entry.OccurredAtUtc);
        Assert.Equal("admin@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Admin, entry.ActorRole);
        Assert.Equal(["Payment amount (SAR)", "Status"], entry.Changes.Select(change => change.Field));
        Assert.Equal((null, "100.00"), (entry.Changes[0].Before, entry.Changes[0].After));
        Assert.Equal(("Sent", "Paid"), (entry.Changes[1].Before, entry.Changes[1].After));
    }

    [Fact]
    public void Each_entry_gets_its_own_id_and_the_sequence_is_left_to_the_database()
    {
        var first = Entry();
        var second = Entry();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(0L, first.Sequence);
    }

    [Fact]
    public void Either_side_of_a_change_may_be_none_but_a_change_keeps_which()
    {
        var entry = Entry(HistoryKind.DraftEdited, changes: [new("Line added: X (X-1)", null, "1.00 x SAR 1.00, VAT 15.00%"), new("Notes", "old", null)]);

        Assert.Null(entry.Changes[0].Before);
        Assert.Null(entry.Changes[1].After);
    }

    [Fact]
    public void Arabic_and_long_text_are_kept_as_written()
    {
        var longText = new string('x', 1000);
        var entry = Entry(changes: [new("Customer", "شركة الأمل", longText)]);

        Assert.Equal("شركة الأمل", entry.Changes[0].Before);
        Assert.Equal(longText, entry.Changes[0].After);
    }

    // ---- what cannot be made ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_without_a_user_cannot_be_made(string email)
    {
        Assert.Throws<ArgumentException>(() => Entry(email: email));
    }

    [Fact]
    public void An_entry_needs_a_real_role()
    {
        Assert.Throws<ArgumentException>(() => Entry(role: (StaffRole)99));
    }

    [Fact]
    public void An_entry_needs_a_real_kind()
    {
        Assert.Throws<ArgumentException>(() => Entry(kind: (HistoryKind)99));
    }

    [Fact]
    public void An_entry_that_changes_nothing_cannot_be_made()
    {
        Assert.Throws<ArgumentException>(() => Entry(changes: []));
    }

    [Fact]
    public void An_email_is_kept_exactly_including_its_case_and_a_padded_one_is_trimmed_never_replaced()
    {
        Assert.Equal("Agent1@CRM.local", Entry(email: "Agent1@CRM.local").ActorEmail);
        Assert.Equal("agent1@crm.local", Entry(email: "  agent1@crm.local  ").ActorEmail);
    }

    // ---- immutable: nothing to change it with ----

    [Fact]
    public void No_property_of_an_entry_can_be_set_from_outside()
    {
        foreach (var property in typeof(InvoiceHistoryEntry).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var setter = property.SetMethod;
            Assert.True(setter is null || !setter.IsPublic, $"{property.Name} has a public setter");
        }
    }

    [Fact]
    public void An_entry_has_no_public_method_that_could_change_it()
    {
        var methods = typeof(InvoiceHistoryEntry)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName) // property getters
            .Select(method => method.Name)
            .ToList();

        Assert.Empty(methods);
    }

    [Fact]
    public void The_only_public_way_in_is_Create_and_there_is_no_public_constructor()
    {
        Assert.Empty(typeof(InvoiceHistoryEntry).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(typeof(InvoiceHistoryEntry).GetMethod(nameof(InvoiceHistoryEntry.Create), BindingFlags.Static | BindingFlags.Public));
    }

    [Fact]
    public void The_list_of_changes_cannot_be_used_to_alter_the_entry()
    {
        var source = new List<HistoryChange> { new("Status", "Draft", "Sent") };
        var entry = Entry(changes: source);

        source.Add(new HistoryChange("Extra", null, "x"));
        Assert.Single(entry.Changes);
        Assert.False(entry.Changes is List<HistoryChange>, "the entry must not hand out a list that can be added to");
        Assert.Throws<NotSupportedException>(() => ((IList<HistoryChange>)entry.Changes).Add(new HistoryChange("Y", null, "y")));
    }

    // ---- a change is a value ----

    [Fact]
    public void Two_changes_with_the_same_parts_are_equal()
    {
        Assert.Equal(new HistoryChange("Status", "Draft", "Sent"), new HistoryChange("Status", "Draft", "Sent"));
        Assert.NotEqual(new HistoryChange("Status", "Draft", "Sent"), new HistoryChange("Status", "Sent", "Draft"));
    }
}
