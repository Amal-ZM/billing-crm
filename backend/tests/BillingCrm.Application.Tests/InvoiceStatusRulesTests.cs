using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The legal changes of an invoice's status (spec 007, data-model.md): exactly four, and only here (spec 008 added Sent to Paid).</summary>
public class InvoiceStatusRulesTests
{
    private static readonly InvoiceStatus[] All = [InvoiceStatus.Draft, InvoiceStatus.Sent, InvoiceStatus.Paid, InvoiceStatus.Cancelled];

    private static readonly (InvoiceStatus From, InvoiceStatus To)[] Legal =
    [
        (InvoiceStatus.Draft, InvoiceStatus.Sent),
        (InvoiceStatus.Draft, InvoiceStatus.Cancelled),
        (InvoiceStatus.Sent, InvoiceStatus.Cancelled),
        (InvoiceStatus.Sent, InvoiceStatus.Paid),
    ];

    [Fact]
    public void The_status_has_exactly_Draft_Sent_Paid_and_Cancelled_and_Overdue_is_not_one_of_them()
    {
        Assert.Equal(
            ["Cancelled", "Draft", "Paid", "Sent"],
            Enum.GetNames<InvoiceStatus>().OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain("Overdue", Enum.GetNames<InvoiceStatus>());
    }

    [Fact]
    public void Every_pair_of_statuses_is_legal_only_when_it_is_one_of_the_four_legal_changes()
    {
        foreach (var from in All)
        {
            foreach (var to in All)
            {
                var expected = Legal.Contains((from, to));
                Assert.True(
                    expected == InvoiceStatusRules.CanChange(from, to),
                    $"{from} to {to} should be {(expected ? "legal" : "illegal")}");
            }
        }
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Sent, InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Sent, InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Paid, InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Paid, InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Cancelled, InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled, InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Cancelled, InvoiceStatus.Cancelled)]
    public void The_named_illegal_changes_are_illegal(InvoiceStatus from, InvoiceStatus to)
    {
        Assert.False(InvoiceStatusRules.CanChange(from, to));
    }

    [Fact]
    public void The_next_statuses_are_what_the_status_permits()
    {
        Assert.Equal([InvoiceStatus.Sent, InvoiceStatus.Cancelled], InvoiceStatusRules.NextStatuses(InvoiceStatus.Draft));
        Assert.Equal([InvoiceStatus.Cancelled, InvoiceStatus.Paid], InvoiceStatusRules.NextStatuses(InvoiceStatus.Sent));
        Assert.Empty(InvoiceStatusRules.NextStatuses(InvoiceStatus.Paid));
        Assert.Empty(InvoiceStatusRules.NextStatuses(InvoiceStatus.Cancelled));
    }

    [Fact]
    public void The_next_statuses_agree_with_CanChange_for_every_status()
    {
        foreach (var from in All)
        {
            Assert.Equal(
                All.Where(to => InvoiceStatusRules.CanChange(from, to)).OrderBy(status => status),
                InvoiceStatusRules.NextStatuses(from).OrderBy(status => status));
        }
    }
}
