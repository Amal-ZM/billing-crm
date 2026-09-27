using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>Paid and remaining (spec 008, the invoice-money skill): exact sums and differences of two-decimal amounts, never rounded.</summary>
public class InvoiceMoneyBalanceTests
{
    [Fact]
    public void With_no_payments_nothing_is_paid_and_the_whole_total_remains()
    {
        var balance = InvoiceMoney.Balance(230.00m, []);

        Assert.Equal(0.00m, balance.Paid);
        Assert.Equal(230.00m, balance.Remaining);
    }

    [Fact]
    public void A_part_payment_leaves_the_rest()
    {
        var balance = InvoiceMoney.Balance(230.00m, [100.00m]);

        Assert.Equal(100.00m, balance.Paid);
        Assert.Equal(130.00m, balance.Remaining);
    }

    [Fact]
    public void Paying_the_exact_total_leaves_exactly_zero()
    {
        var balance = InvoiceMoney.Balance(230.00m, [100.00m, 130.00m]);

        Assert.Equal(230.00m, balance.Paid);
        Assert.Equal(0.00m, balance.Remaining);
    }

    [Fact]
    public void Twenty_three_payments_of_ten_settle_230_exactly()
    {
        var balance = InvoiceMoney.Balance(230.00m, Enumerable.Repeat(10.00m, 23));

        Assert.Equal(0.00m, balance.Remaining);
    }

    [Fact]
    public void Awkward_halala_amounts_add_up_with_no_drift()
    {
        // 76.67 + 76.67 + 76.66 = 230.00; three of 0.01 leave 229.97.
        Assert.Equal(0.00m, InvoiceMoney.Balance(230.00m, [76.67m, 76.67m, 76.66m]).Remaining);
        Assert.Equal(229.97m, InvoiceMoney.Balance(230.00m, [0.01m, 0.01m, 0.01m]).Remaining);
    }

    [Fact]
    public void Many_small_payments_never_drift_from_the_total()
    {
        // 1,000 payments of 0.01 make exactly 10.00 (a float would not).
        var balance = InvoiceMoney.Balance(10.00m, Enumerable.Repeat(0.01m, 1000));

        Assert.Equal(10.00m, balance.Paid);
        Assert.Equal(0.00m, balance.Remaining);
    }

    [Fact]
    public void A_grand_total_of_zero_has_nothing_remaining()
    {
        var balance = InvoiceMoney.Balance(0.00m, []);

        Assert.Equal(0.00m, balance.Paid);
        Assert.Equal(0.00m, balance.Remaining);
    }

    [Fact]
    public void Nothing_is_rounded_and_the_figures_keep_two_decimals()
    {
        var balance = InvoiceMoney.Balance(230.00m, [100.10m]);

        Assert.Equal(100.10m, balance.Paid);
        Assert.Equal(129.90m, balance.Remaining);
        Assert.Equal(2, balance.Remaining.Scale);
        Assert.Equal(2, balance.Paid.Scale);
    }

    [Fact]
    public void An_overpaid_state_is_reported_as_it_is_not_hidden()
    {
        // The application can never produce this; if stored data ever did, it must show, not be clamped.
        Assert.Equal(-10.00m, InvoiceMoney.Balance(100.00m, [110.00m]).Remaining);
    }
}
