using System.Globalization;
using BillingCrm.Application.Auth;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>What each history entry says (spec 010): pure, no stores. Every value is text as it is at that moment, formatted and never calculated.</summary>
public class InvoiceHistoryBuilderTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Due = new(2030, 7, 1);
    private static readonly DateOnly PayDate = new(2030, 6, 2);
    private static readonly Actor Agent = new("agent1@crm.local", StaffRole.Agent);
    private static readonly Actor Admin = new("admin@crm.local", StaffRole.Admin);
    private static readonly Guid InvoiceId = Guid.NewGuid();
    private static readonly Guid CustomerA = Guid.NewGuid();
    private static readonly Guid CustomerB = Guid.NewGuid();

    private static Product Consulting() => Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100.00m, 15m, Now);

    private static Product Arabic() => Product.Create("خدمة استشارية", "SVC-1", ProductUnit.Service, 50.50m, 5m, Now);

    private static Invoice Draft(string? notes = "Net 30", decimal discount = 0m)
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, notes, Now, discount);
        invoice.AddLine(Consulting(), 2m, Now);
        return invoice;
    }

    private static InvoiceHistoryLine Line(string name = "Consulting hour", string sku = "CNS-001", decimal quantity = 2m, decimal price = 100m, decimal vat = 15m, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, sku, quantity, price, vat);

    private static InvoiceHistorySnapshot Snapshot(
        Guid? customerId = null, string customerName = "ABC", DateOnly? due = null, string? notes = "Net 30", decimal discount = 0m, InvoiceHistoryLine[]? lines = null) =>
        new(InvoiceId, customerId ?? CustomerA, customerName, due ?? Due, notes, discount, lines ?? []);

    private static string Describe(InvoiceHistoryEntry entry) =>
        string.Join(" | ", entry.Changes.Select(change => $"{change.Field}: {change.Before ?? "(none)"} -> {change.After ?? "(none)"}"));

    // ---- Created ----

    [Fact]
    public void Created_records_the_customer_the_due_date_the_notes_and_every_line_as_at_that_moment()
    {
        var invoice = Draft();

        var entry = InvoiceHistoryBuilder.Created(invoice, "ABC Trading", Agent, Now);

        Assert.Equal(HistoryKind.Created, entry.Kind);
        Assert.Equal(invoice.Id, entry.InvoiceId);
        Assert.Equal(
            [
                new HistoryChange("Customer", null, "ABC Trading"),
                new HistoryChange("Due date", null, "2030-07-01"),
                new HistoryChange("Notes", null, "Net 30"),
                new HistoryChange("Line: Consulting hour (CNS-001)", null, "2.00 x SAR 100.00, VAT 15.00%"),
            ],
            entry.Changes);
    }

    [Fact]
    public void Created_leaves_out_notes_when_there_are_none_and_the_discount_when_it_is_zero()
    {
        var entry = InvoiceHistoryBuilder.Created(Draft(notes: null), "ABC", Agent, Now);

        Assert.DoesNotContain(entry.Changes, change => change.Field == "Notes");
        Assert.DoesNotContain(entry.Changes, change => change.Field == "Discount (SAR)");
    }

    [Fact]
    public void Created_records_a_discount_above_zero_as_exact_money_text()
    {
        var entry = InvoiceHistoryBuilder.Created(Draft(discount: 20m), "ABC", Agent, Now);

        Assert.Contains(new HistoryChange("Discount (SAR)", null, "20.00"), entry.Changes);
    }

    [Fact]
    public void Created_records_one_change_per_line_in_order_and_keeps_Arabic_names()
    {
        var invoice = Draft();
        invoice.AddLine(Arabic(), 1.5m, Now);

        var entry = InvoiceHistoryBuilder.Created(invoice, "شركة ABC", Agent, Now);

        Assert.Contains(new HistoryChange("Customer", null, "شركة ABC"), entry.Changes);
        var lines = entry.Changes.Where(change => change.Field.StartsWith("Line:", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                new HistoryChange("Line: Consulting hour (CNS-001)", null, "2.00 x SAR 100.00, VAT 15.00%"),
                new HistoryChange("Line: خدمة استشارية (SVC-1)", null, "1.50 x SAR 50.50, VAT 5.00%"),
            ],
            lines);
    }

    [Fact]
    public void Created_carries_the_given_user_and_time()
    {
        var entry = InvoiceHistoryBuilder.Created(Draft(), "ABC", Admin, Now);

        Assert.Equal("admin@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Admin, entry.ActorRole);
        Assert.Equal(Now, entry.OccurredAtUtc);
    }

    // ---- Draft edited: only what changed ----

    [Fact]
    public void An_edit_that_changes_nothing_makes_no_entry()
    {
        var line = Line();
        var before = Snapshot(lines: [line]);
        var after = Snapshot(lines: [line]);

        Assert.Null(InvoiceHistoryBuilder.DraftEdited(before, after, Agent, Now));
    }

    [Fact]
    public void Only_the_customer_changed_records_only_the_customer_by_name()
    {
        var line = Line();

        var entry = InvoiceHistoryBuilder.DraftEdited(
            Snapshot(CustomerA, "ABC", lines: [line]), Snapshot(CustomerB, "XYZ Co", lines: [line]), Agent, Now)!;

        Assert.Equal(HistoryKind.DraftEdited, entry.Kind);
        Assert.Equal([new HistoryChange("Customer", "ABC", "XYZ Co")], entry.Changes);
    }

    [Fact]
    public void A_different_customer_with_the_same_name_is_still_a_change()
    {
        var line = Line();

        var entry = InvoiceHistoryBuilder.DraftEdited(
            Snapshot(CustomerA, "ABC", lines: [line]), Snapshot(CustomerB, "ABC", lines: [line]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Customer", "ABC", "ABC")], entry.Changes);
    }

    [Fact]
    public void Only_the_due_date_changed_records_only_the_due_date()
    {
        var line = Line();

        var entry = InvoiceHistoryBuilder.DraftEdited(
            Snapshot(lines: [line]), Snapshot(due: new DateOnly(2030, 8, 15), lines: [line]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Due date", "2030-07-01", "2030-08-15")], entry.Changes);
    }

    [Fact]
    public void Notes_changed_cleared_and_added_are_each_recorded_as_text_or_none()
    {
        var line = Line();

        var changed = InvoiceHistoryBuilder.DraftEdited(Snapshot(notes: "a", lines: [line]), Snapshot(notes: "b", lines: [line]), Agent, Now)!;
        var cleared = InvoiceHistoryBuilder.DraftEdited(Snapshot(notes: "a", lines: [line]), Snapshot(notes: null, lines: [line]), Agent, Now)!;
        var added = InvoiceHistoryBuilder.DraftEdited(Snapshot(notes: null, lines: [line]), Snapshot(notes: "b", lines: [line]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Notes", "a", "b")], changed.Changes);
        Assert.Equal([new HistoryChange("Notes", "a", null)], cleared.Changes);
        Assert.Equal([new HistoryChange("Notes", null, "b")], added.Changes);
    }

    [Fact]
    public void Discount_changed_cleared_and_added_are_recorded_as_exact_money_text_or_none()
    {
        var line = Line();

        var changed = InvoiceHistoryBuilder.DraftEdited(Snapshot(discount: 20m, lines: [line]), Snapshot(discount: 25.5m, lines: [line]), Agent, Now)!;
        var cleared = InvoiceHistoryBuilder.DraftEdited(Snapshot(discount: 20m, lines: [line]), Snapshot(discount: 0m, lines: [line]), Agent, Now)!;
        var added = InvoiceHistoryBuilder.DraftEdited(Snapshot(discount: 0m, lines: [line]), Snapshot(discount: 20m, lines: [line]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Discount (SAR)", "20.00", "25.50")], changed.Changes);
        Assert.Equal([new HistoryChange("Discount (SAR)", "20.00", null)], cleared.Changes);
        Assert.Equal([new HistoryChange("Discount (SAR)", null, "20.00")], added.Changes);
    }

    [Fact]
    public void A_line_added_records_what_it_was_saved_with()
    {
        var kept = Line();
        var added = Line("خدمة استشارية", "SVC-1", 1.5m, 50.5m, 5m);

        var entry = InvoiceHistoryBuilder.DraftEdited(Snapshot(lines: [kept]), Snapshot(lines: [kept, added]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Line added: خدمة استشارية (SVC-1)", null, "1.50 x SAR 50.50, VAT 5.00%")], entry.Changes);
    }

    [Fact]
    public void A_line_removed_records_what_it_had()
    {
        var kept = Line();
        var removed = Line("Web design", "WEB-1", 3m, 200m, 15m);

        var entry = InvoiceHistoryBuilder.DraftEdited(Snapshot(lines: [kept, removed]), Snapshot(lines: [kept]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Line removed: Web design (WEB-1)", "3.00 x SAR 200.00, VAT 15.00%", null)], entry.Changes);
    }

    [Fact]
    public void A_quantity_changed_records_the_old_and_new_quantity()
    {
        var id = Guid.NewGuid();

        var entry = InvoiceHistoryBuilder.DraftEdited(
            Snapshot(lines: [Line(quantity: 2m, id: id)]), Snapshot(lines: [Line(quantity: 5m, id: id)]), Agent, Now)!;

        Assert.Equal([new HistoryChange("Line quantity: Consulting hour (CNS-001)", "2.00", "5.00")], entry.Changes);
    }

    [Fact]
    public void Everything_changed_in_one_edit_is_one_entry_with_one_list()
    {
        var id = Guid.NewGuid();
        var removed = Line("Web design", "WEB-1", 3m, 200m, 15m);
        var added = Line("Hosting", "HOST-1", 1m, 10m, 15m);

        var entry = InvoiceHistoryBuilder.DraftEdited(
            Snapshot(CustomerA, "ABC", Due, "a", 5m, [Line(quantity: 2m, id: id), removed]),
            Snapshot(CustomerB, "XYZ", new DateOnly(2030, 9, 1), "b", 0m, [Line(quantity: 4m, id: id), added]),
            Agent,
            Now)!;

        Assert.Equal(
            "Customer: ABC -> XYZ | Due date: 2030-07-01 -> 2030-09-01 | Notes: a -> b | Discount (SAR): 5.00 -> (none) | " +
            "Line quantity: Consulting hour (CNS-001): 2.00 -> 4.00 | Line added: Hosting (HOST-1): (none) -> 1.00 x SAR 10.00, VAT 15.00% | " +
            "Line removed: Web design (WEB-1): 3.00 x SAR 200.00, VAT 15.00% -> (none)",
            Describe(entry));
    }

    [Fact]
    public void An_edit_touching_100_lines_at_once_is_still_one_entry()
    {
        // The largest an invoice may hold (spec 005's edge case): every one of the 100 lines has its quantity changed and one line is
        // added, so this is the biggest DraftEdited diff the builder ever has to build. One save is still one entry.
        var before = Enumerable.Range(0, 100).Select(i => Line(quantity: 1m, id: Guid.NewGuid())).ToArray();
        var after = before.Select(line => line with { Quantity = line.Quantity + 1m }).Append(Line("Hosting", "HOST-1", 1m, 10m, 15m)).ToArray();

        var entry = InvoiceHistoryBuilder.DraftEdited(Snapshot(lines: before), Snapshot(lines: after), Agent, Now)!;

        Assert.Equal(101, entry.Changes.Count);
        Assert.Equal(100, entry.Changes.Count(change => change.Field.StartsWith("Line quantity:", StringComparison.Ordinal)));
        Assert.Single(entry.Changes, change => change.Field.StartsWith("Line added:", StringComparison.Ordinal));
    }

    [Fact]
    public void Lines_are_matched_by_id_not_by_position_or_name_so_two_lines_of_one_product_stay_apart()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        // Same product twice; only the second one's quantity changes. Swapped order in the "after" list must not confuse the match.
        var entry = InvoiceHistoryBuilder.DraftEdited(
            Snapshot(lines: [Line(quantity: 1m, id: first), Line(quantity: 2m, id: second)]),
            Snapshot(lines: [Line(quantity: 7m, id: second), Line(quantity: 1m, id: first)]),
            Agent,
            Now)!;

        Assert.Equal([new HistoryChange("Line quantity: Consulting hour (CNS-001)", "2.00", "7.00")], entry.Changes);
    }

    [Fact]
    public void An_edit_entry_carries_the_invoice_the_user_and_the_time()
    {
        var entry = InvoiceHistoryBuilder.DraftEdited(Snapshot(notes: "a", lines: [Line()]), Snapshot(notes: "b", lines: [Line()]), Admin, Now)!;

        Assert.Equal(InvoiceId, entry.InvoiceId);
        Assert.Equal("admin@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Admin, entry.ActorRole);
        Assert.Equal(Now, entry.OccurredAtUtc);
    }

    [Fact]
    public void A_snapshot_of_an_invoice_holds_its_header_and_its_lines_with_their_saved_values()
    {
        var invoice = Draft(discount: 10m);

        var snapshot = InvoiceHistorySnapshot.From(invoice, "ABC");

        Assert.Equal(invoice.Id, snapshot.InvoiceId);
        Assert.Equal(invoice.CustomerId, snapshot.CustomerId);
        Assert.Equal("ABC", snapshot.CustomerName);
        Assert.Equal(Due, snapshot.DueDate);
        Assert.Equal("Net 30", snapshot.Notes);
        Assert.Equal(10m, snapshot.Discount);
        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(invoice.Lines[0].Id, line.Id);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal("CNS-001", line.Sku);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public void The_text_is_the_same_under_any_culture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            var expected = Describe(InvoiceHistoryBuilder.Created(Draft(discount: 1234.5m), "ABC", Agent, Now));

            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var actual = Describe(InvoiceHistoryBuilder.Created(Draft(discount: 1234.5m), "ABC", Agent, Now));

            Assert.Equal(expected, actual);
            Assert.Contains("Discount (SAR): (none) -> 1234.50", actual);
            Assert.Contains("Due date: (none) -> 2030-07-01", actual);
            Assert.Contains("2.00 x SAR 100.00, VAT 15.00%", actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void A_long_note_is_kept_whole()
    {
        var note = new string('س', 1000);
        var line = Line();

        var entry = InvoiceHistoryBuilder.DraftEdited(Snapshot(notes: null, lines: [line]), Snapshot(notes: note, lines: [line]), Agent, Now)!;

        Assert.Equal(note, entry.Changes.Single().After);
    }

    // ---- Sent ----

    [Fact]
    public void Sent_records_the_status_change_from_Draft_to_Sent()
    {
        var entry = InvoiceHistoryBuilder.Sent(InvoiceId, Agent, Now);

        Assert.Equal(HistoryKind.Sent, entry.Kind);
        Assert.Equal(InvoiceId, entry.InvoiceId);
        Assert.Equal([new HistoryChange("Status", "Draft", "Sent")], entry.Changes);
        Assert.Equal("agent1@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Agent, entry.ActorRole);
        Assert.Equal(Now, entry.OccurredAtUtc);
    }

    // ---- Payment ----

    private static Payment PaymentOf(decimal amount, PaymentMethod method = PaymentMethod.Cash, string? note = null)
    {
        var invoice = Draft();
        Assert.True(invoice.Send(Now));
        return invoice.RecordPayment(PayDate, amount, method, note, Now);
    }

    [Fact]
    public void A_part_payment_records_the_payment_and_the_balance_before_and_after_and_no_status()
    {
        var payment = PaymentOf(100m, PaymentMethod.Cash, "first half / الدفعة الأولى");

        var entry = InvoiceHistoryBuilder.PaymentRecorded(payment, 0m, 230m, 100m, 130m, settled: false, Agent, Now);

        Assert.Equal(HistoryKind.Payment, entry.Kind);
        Assert.Equal(payment.InvoiceId, entry.InvoiceId);
        Assert.Equal(
            [
                new HistoryChange("Payment date", null, "2030-06-02"),
                new HistoryChange("Payment amount (SAR)", null, "100.00"),
                new HistoryChange("Payment method", null, "Cash"),
                new HistoryChange("Payment note", null, "first half / الدفعة الأولى"),
                new HistoryChange("Amount paid (SAR)", "0.00", "100.00"),
                new HistoryChange("Amount remaining (SAR)", "230.00", "130.00"),
            ],
            entry.Changes);
        Assert.DoesNotContain(entry.Changes, change => change.Field == "Status");
    }

    [Fact]
    public void A_payment_without_a_note_records_no_note_change()
    {
        var entry = InvoiceHistoryBuilder.PaymentRecorded(PaymentOf(100m, PaymentMethod.Bank), 0m, 230m, 100m, 130m, false, Agent, Now);

        Assert.DoesNotContain(entry.Changes, change => change.Field == "Payment note");
        Assert.Contains(new HistoryChange("Payment method", null, "Bank"), entry.Changes);
    }

    [Fact]
    public void The_payment_that_settles_the_invoice_is_one_Payment_entry_that_also_shows_Sent_to_Paid()
    {
        var entry = InvoiceHistoryBuilder.PaymentRecorded(PaymentOf(130m), 100m, 130m, 230m, 0m, settled: true, Admin, Now);

        Assert.Equal(HistoryKind.Payment, entry.Kind);
        Assert.Contains(new HistoryChange("Amount paid (SAR)", "100.00", "230.00"), entry.Changes);
        Assert.Contains(new HistoryChange("Amount remaining (SAR)", "130.00", "0.00"), entry.Changes);
        Assert.Equal(new HistoryChange("Status", "Sent", "Paid"), entry.Changes[^1]);
        Assert.Single(entry.Changes, change => change.Field == "Status");
    }

    [Fact]
    public void Payment_money_is_always_two_decimals()
    {
        var entry = InvoiceHistoryBuilder.PaymentRecorded(PaymentOf(100.1m), 0m, 230m, 100.1m, 129.9m, false, Agent, Now);

        Assert.Contains(new HistoryChange("Payment amount (SAR)", null, "100.10"), entry.Changes);
        Assert.Contains(new HistoryChange("Amount paid (SAR)", "0.00", "100.10"), entry.Changes);
        Assert.Contains(new HistoryChange("Amount remaining (SAR)", "230.00", "129.90"), entry.Changes);
    }

    [Fact]
    public void A_long_payment_note_is_kept_whole()
    {
        var note = new string('x', 1000);

        var entry = InvoiceHistoryBuilder.PaymentRecorded(PaymentOf(10m, PaymentMethod.Other, note), 0m, 230m, 10m, 220m, false, Agent, Now);

        Assert.Contains(new HistoryChange("Payment note", null, note), entry.Changes);
    }

    // ---- Cancelled ----

    [Theory]
    [InlineData(InvoiceStatus.Draft, "Draft")]
    [InlineData(InvoiceStatus.Sent, "Sent")]
    public void Cancelled_records_the_status_it_came_from_and_Cancelled(InvoiceStatus from, string text)
    {
        var entry = InvoiceHistoryBuilder.Cancelled(InvoiceId, from, Admin, Now);

        Assert.Equal(HistoryKind.Cancelled, entry.Kind);
        Assert.Equal([new HistoryChange("Status", text, "Cancelled")], entry.Changes);
        Assert.Equal("admin@crm.local", entry.ActorEmail);
        Assert.Equal(StaffRole.Admin, entry.ActorRole);
        Assert.Equal(Now, entry.OccurredAtUtc);
    }

    [Fact]
    public void A_late_Sent_invoice_still_reads_Sent_never_Overdue()
    {
        // Overdue is worked out from the due date and is never an event: the builder only ever sees the stored status.
        var entry = InvoiceHistoryBuilder.Cancelled(InvoiceId, InvoiceStatus.Sent, Agent, Now);

        Assert.Equal("Sent", entry.Changes.Single().Before);
        Assert.DoesNotContain("Overdue", Describe(entry));
    }
}
