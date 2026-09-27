using System.Globalization;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>
/// The money rules of an invoice (spec 006, data-model.md, the <c>invoice-money</c> skill), proven with exact decimals: every
/// assertion compares <c>decimal</c> values for equality; the one bound used (a share within one halala of its exact proportional part) is exact too.
/// </summary>
public class InvoiceMoneyTests
{
    private static AmountLine Line(decimal quantity, decimal unitPrice, decimal vatPercent) => new(quantity, unitPrice, vatPercent);

    private static InvoiceAmounts Calculate(decimal discount, params AmountLine[] lines) => InvoiceMoney.Calculate(lines, discount);

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    // ---- the worked examples of data-model.md (A to I) ----

    [Fact]
    public void A_two_at_100_with_15_percent_VAT_and_no_discount_is_200_30_230()
    {
        var amounts = Calculate(0m, Line(2m, 100.00m, 15m));

        Assert.Equal(200.00m, amounts.Lines[0].Net);
        Assert.Equal(0.00m, amounts.Lines[0].DiscountShare);
        Assert.Equal(30.00m, amounts.Lines[0].Vat);
        Assert.Equal(200.00m, amounts.TotalNet);
        Assert.Equal(0.00m, amounts.Discount);
        Assert.Equal(200.00m, amounts.NetAfterDiscount);
        Assert.Equal(30.00m, amounts.TotalVat);
        Assert.Equal(230.00m, amounts.GrandTotal);
    }

    [Fact]
    public void Every_amount_has_exactly_two_decimals_even_when_the_inputs_have_none_or_a_net_has_four()
    {
        var four = Calculate(0m, Line(1.25m, 10.55m, 15m));
        Assert.Equal("13.19", Text(four.Lines[0].Net));
        Assert.Equal("1.98", Text(four.Lines[0].Vat));
        Assert.Equal("15.17", Text(four.GrandTotal));

        var amounts = Calculate(0m, Line(2m, 100m, 15m));

        Assert.Equal("200.00", Text(amounts.Lines[0].Net));
        Assert.Equal("0.00", Text(amounts.Lines[0].DiscountShare));
        Assert.Equal("30.00", Text(amounts.Lines[0].Vat));
        Assert.Equal("200.00", Text(amounts.TotalNet));
        Assert.Equal("0.00", Text(amounts.Discount));
        Assert.Equal("200.00", Text(amounts.NetAfterDiscount));
        Assert.Equal("30.00", Text(amounts.TotalVat));
        Assert.Equal("230.00", Text(amounts.GrandTotal));
    }

    [Fact]
    public void B_lines_at_different_VAT_percentages_each_use_their_own_percentage()
    {
        var amounts = Calculate(0m, Line(2m, 100.00m, 15m), Line(1m, 50.00m, 0m));

        Assert.Equal([200.00m, 50.00m], amounts.Lines.Select(line => line.Net));
        Assert.Equal([30.00m, 0.00m], amounts.Lines.Select(line => line.Vat));
        Assert.Equal(250.00m, amounts.TotalNet);
        Assert.Equal(30.00m, amounts.TotalVat);
        Assert.Equal(280.00m, amounts.GrandTotal);
    }

    [Fact]
    public void C_a_net_with_four_decimals_is_rounded_once_and_the_VAT_is_worked_out_on_the_rounded_net()
    {
        // 1.25 x 10.55 = 13.1875 -> 13.19; 15% of 13.19 = 1.9785 -> 1.98 (so what is shown is what is added up).
        var amounts = Calculate(0m, Line(1.25m, 10.55m, 15m));

        Assert.Equal(13.19m, amounts.Lines[0].Net);
        Assert.Equal(1.98m, amounts.Lines[0].Vat);
        Assert.Equal(13.19m, amounts.TotalNet);
        Assert.Equal(1.98m, amounts.TotalVat);
        Assert.Equal(15.17m, amounts.GrandTotal);
    }

    [Fact]
    public void D_a_half_halala_rounds_away_from_zero_for_the_net_and_a_small_VAT_rounds_down()
    {
        // 0.5 x 0.05 = 0.025 -> 0.03 (not the banker's 0.02); 15% of 0.03 = 0.0045 -> 0.00.
        var amounts = Calculate(0m, Line(0.5m, 0.05m, 15m));

        Assert.Equal(0.03m, amounts.Lines[0].Net);
        Assert.Equal(0.00m, amounts.Lines[0].Vat);
        Assert.Equal(0.03m, amounts.GrandTotal);
    }

    [Fact]
    public void E_a_discount_is_taken_off_the_net_before_VAT()
    {
        var amounts = Calculate(20.00m, Line(2m, 100.00m, 15m));

        Assert.Equal(200.00m, amounts.TotalNet);
        Assert.Equal(20.00m, amounts.Lines[0].DiscountShare);
        Assert.Equal(20.00m, amounts.Discount);
        Assert.Equal(180.00m, amounts.NetAfterDiscount);
        Assert.Equal(27.00m, amounts.Lines[0].Vat);
        Assert.Equal(27.00m, amounts.TotalVat);
        Assert.Equal(207.00m, amounts.GrandTotal);
    }

    [Fact]
    public void F_a_discount_is_shared_in_proportion_to_the_line_nets_and_each_line_pays_VAT_on_its_own_reduced_net()
    {
        var amounts = Calculate(30.00m, Line(1m, 100.00m, 15m), Line(1m, 50.00m, 0m));

        Assert.Equal([100.00m, 50.00m], amounts.Lines.Select(line => line.Net));
        Assert.Equal([20.00m, 10.00m], amounts.Lines.Select(line => line.DiscountShare));
        Assert.Equal([12.00m, 0.00m], amounts.Lines.Select(line => line.Vat));
        Assert.Equal(150.00m, amounts.TotalNet);
        Assert.Equal(120.00m, amounts.NetAfterDiscount);
        Assert.Equal(12.00m, amounts.TotalVat);
        Assert.Equal(132.00m, amounts.GrandTotal);
    }

    [Fact]
    public void G_one_halala_over_three_equal_lines_goes_to_the_first_line()
    {
        var amounts = Calculate(0.01m, Line(1m, 10.00m, 15m), Line(1m, 10.00m, 15m), Line(1m, 10.00m, 15m));

        Assert.Equal([0.01m, 0.00m, 0.00m], amounts.Lines.Select(line => line.DiscountShare));
        // Line 1 pays VAT on 9.99: 1.4985 -> 1.50 (away from zero); the others on 10.00: 1.50.
        Assert.Equal([1.50m, 1.50m, 1.50m], amounts.Lines.Select(line => line.Vat));
        Assert.Equal(30.00m, amounts.TotalNet);
        Assert.Equal(29.99m, amounts.NetAfterDiscount);
        Assert.Equal(4.50m, amounts.TotalVat);
        Assert.Equal(34.49m, amounts.GrandTotal);
    }

    [Fact]
    public void H_a_discount_equal_to_the_whole_net_leaves_nothing_to_pay()
    {
        var amounts = Calculate(200.00m, Line(2m, 100.00m, 15m));

        Assert.Equal(200.00m, amounts.Lines[0].DiscountShare);
        Assert.Equal(0.00m, amounts.NetAfterDiscount);
        Assert.Equal(0.00m, amounts.TotalVat);
        Assert.Equal(0.00m, amounts.GrandTotal);
    }

    [Fact]
    public void I_a_free_item_has_no_amounts_and_does_not_disturb_the_other_lines()
    {
        var amounts = Calculate(0m, Line(1m, 0.00m, 15m), Line(2m, 100.00m, 15m));

        Assert.Equal([0.00m, 200.00m], amounts.Lines.Select(line => line.Net));
        Assert.Equal([0.00m, 30.00m], amounts.Lines.Select(line => line.Vat));
        Assert.Equal(200.00m, amounts.TotalNet);
        Assert.Equal(30.00m, amounts.TotalVat);
        Assert.Equal(230.00m, amounts.GrandTotal);
    }

    // ---- sharing the discount ----

    [Fact]
    public void The_left_over_halalas_go_to_the_largest_remainders_and_the_shares_add_up_to_the_discount()
    {
        // Nets 10.00, 20.00, 30.00 (total 60.00), discount 0.10: exact shares 0.1666.., 0.3333.., 0.5000.
        // Floors 0.01 + 0.03 + 0.05 = 0.09, so one halala is left; the first line has the largest remainder.
        var amounts = Calculate(0.10m, Line(1m, 10.00m, 0m), Line(1m, 20.00m, 0m), Line(1m, 30.00m, 0m));

        Assert.Equal([0.02m, 0.03m, 0.05m], amounts.Lines.Select(line => line.DiscountShare));
        Assert.Equal(0.10m, amounts.Lines.Sum(line => line.DiscountShare));
    }

    [Fact]
    public void Equal_remainders_are_broken_in_favour_of_the_earlier_line()
    {
        // Two lines of 0.03 and a discount of 0.01: both remainders are equal, so the first line gets the halala.
        var amounts = Calculate(0.01m, Line(1m, 0.03m, 0m), Line(1m, 0.03m, 0m));

        Assert.Equal([0.01m, 0.00m], amounts.Lines.Select(line => line.DiscountShare));
    }

    [Fact]
    public void A_zero_discount_over_zero_net_lines_gives_zero_shares_without_dividing_by_zero()
    {
        var amounts = Calculate(0m, Line(1m, 0.00m, 15m), Line(3m, 0.00m, 0m));

        Assert.Equal([0.00m, 0.00m], amounts.Lines.Select(line => line.DiscountShare));
        Assert.Equal(0.00m, amounts.GrandTotal);
    }

    // ---- rounding is once per line, never on a running total ----

    [Fact]
    public void VAT_is_rounded_per_line_and_never_on_the_total()
    {
        // Each line: net 0.03, VAT 0.0045 -> 0.00. Rounding the total instead would give 0.0135 -> 0.01.
        var amounts = Calculate(0m, Line(0.5m, 0.05m, 15m), Line(0.5m, 0.05m, 15m), Line(0.5m, 0.05m, 15m));

        Assert.Equal(0.09m, amounts.TotalNet);
        Assert.Equal(0.00m, amounts.TotalVat);
        Assert.Equal(0.09m, amounts.GrandTotal);
    }

    [Theory]
    [InlineData("0.5", "0.05", "0.03")]      // 0.025 -> 0.03
    [InlineData("1.25", "10.55", "13.19")]   // 13.1875 -> 13.19
    [InlineData("0.01", "0.50", "0.01")]     // 0.0050 -> 0.01
    [InlineData("0.01", "0.49", "0.00")]     // 0.0049 -> 0.00
    [InlineData("2.5", "0.41", "1.03")]      // 1.025 -> 1.03
    public void A_line_net_is_rounded_half_away_from_zero(string quantity, string unitPrice, string expectedNet)
    {
        var amounts = Calculate(0m, Line(Parse(quantity), Parse(unitPrice), 0m));

        Assert.Equal(Parse(expectedNet), amounts.Lines[0].Net);
    }

    [Theory]
    [InlineData("13.19", "15", "1.98")]      // 1.9785 -> 1.98
    [InlineData("0.03", "15", "0.00")]       // 0.0045 -> 0.00
    [InlineData("9.99", "15", "1.50")]       // 1.4985 -> 1.50
    [InlineData("10.00", "5.5", "0.55")]     // 0.55 exactly
    [InlineData("0.10", "5", "0.01")]        // 0.005 -> 0.01
    public void A_line_VAT_is_rounded_half_away_from_zero(string net, string vatPercent, string expectedVat)
    {
        var amounts = Calculate(0m, Line(1m, Parse(net), Parse(vatPercent)));

        Assert.Equal(Parse(expectedVat), amounts.Lines[0].Vat);
    }

    private static decimal Parse(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    // ---- edges and programming errors ----

    [Fact]
    public void No_lines_give_all_zeros()
    {
        var amounts = Calculate(0m);

        Assert.Empty(amounts.Lines);
        Assert.Equal(0.00m, amounts.TotalNet);
        Assert.Equal(0.00m, amounts.Discount);
        Assert.Equal(0.00m, amounts.NetAfterDiscount);
        Assert.Equal(0.00m, amounts.TotalVat);
        Assert.Equal(0.00m, amounts.GrandTotal);
    }

    [Fact]
    public void A_negative_discount_is_a_programming_error_because_the_service_refuses_it_first()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(-0.01m, Line(2m, 100.00m, 15m)));
    }

    [Fact]
    public void A_discount_above_the_total_net_is_a_programming_error_because_the_service_refuses_it_first()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(200.01m, Line(2m, 100.00m, 15m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(0.01m));
    }

    [Fact]
    public void The_ceiling_is_ten_trillion_less_one_halala()
    {
        Assert.Equal(9_999_999_999_999.99m, InvoiceMoney.MaxAmount);
    }

    [Fact]
    public void The_largest_possible_line_is_calculated_exactly_and_is_far_above_the_ceiling_so_the_service_can_refuse_it()
    {
        // The largest allowed quantity and price (spec 004/005): 999,999.99 x 999,999,999.99.
        var amounts = Calculate(0m, Line(999_999.99m, 999_999_999.99m, 100m));

        // 999,999.99 x 999,999,999.99 = 999,999,989,990,000.0001 exactly, so the net is 999,999,989,990,000.00.
        Assert.Equal(999_999_989_990_000.00m, amounts.Lines[0].Net);
        Assert.Equal(999_999_989_990_000.00m, amounts.Lines[0].Vat);
        Assert.Equal(1_999_999_979_980_000.00m, amounts.GrandTotal);
        Assert.True(amounts.Lines[0].Net > InvoiceMoney.MaxAmount);
    }

    [Fact]
    public void A_hundred_lines_near_the_ceiling_with_a_large_discount_are_shared_exactly()
    {
        // 100 lines of net 99,999,999,999.99 make a total net of 9,999,999,999,999.00, just under the ceiling.
        var lines = Enumerable.Range(0, 100).Select(_ => Line(1m, 99_999_999_999.99m, 15m)).ToArray();

        var amounts = Calculate(5_000_000_000_000.55m, lines);

        Assert.Equal(9_999_999_999_999.00m, amounts.TotalNet);
        Assert.Equal(5_000_000_000_000.55m, amounts.Lines.Sum(line => line.DiscountShare));
        Assert.All(amounts.Lines, line => Assert.InRange(line.DiscountShare, 50_000_000_000.00m, 50_000_000_000.01m));
        Assert.Equal(amounts.NetAfterDiscount + amounts.TotalVat, amounts.GrandTotal);
    }

    // ---- invariants over many generated invoices (fixed seed: the same invoices every run) ----

    [Fact]
    public void Over_thousands_of_generated_invoices_the_amounts_always_add_up_exactly()
    {
        var random = new Random(20260924);

        for (var run = 0; run < 3000; run++)
        {
            var lines = new List<AmountLine>();
            var lineCount = run % 7 == 0 ? random.Next(1, 101) : random.Next(1, 9);
            for (var i = 0; i < lineCount; i++)
            {
                var quantity = random.Next(1, 100_000) / 100m;
                // Every fifth line is free, so zero-net lines are mixed with a non-zero discount.
                var unitPrice = i % 5 == 4 ? 0m : random.Next(0, 10_000_000) / 100m;
                var vat = random.Next(0, 10_001) / 100m;
                lines.Add(new AmountLine(quantity, unitPrice, vat));
            }

            var totalNet = InvoiceMoney.Calculate(lines, 0m).TotalNet;
            var netInHalalas = (long)(totalNet * 100m);
            var discount = run % 10 == 0
                ? totalNet
                : run % 10 == 1 ? 0m : random.NextInt64(0, netInHalalas + 1) / 100m;

            var amounts = InvoiceMoney.Calculate(lines, discount);

            Assert.Equal(lines.Count, amounts.Lines.Count);
            Assert.Equal(amounts.Lines.Sum(line => line.Net), amounts.TotalNet);
            Assert.Equal(amounts.Lines.Sum(line => line.DiscountShare), amounts.Discount);
            Assert.Equal(discount, amounts.Discount);
            Assert.Equal(amounts.Lines.Sum(line => line.Vat), amounts.TotalVat);
            Assert.Equal(amounts.TotalNet - amounts.Discount, amounts.NetAfterDiscount);
            Assert.Equal(amounts.NetAfterDiscount + amounts.TotalVat, amounts.GrandTotal);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = amounts.Lines[i];
                Assert.Equal(decimal.Round(lines[i].Quantity * lines[i].UnitPrice, 2, MidpointRounding.AwayFromZero), line.Net);
                Assert.InRange(line.DiscountShare, 0m, line.Net);
                Assert.Equal(
                    decimal.Round((line.Net - line.DiscountShare) * lines[i].VatPercent / 100m, 2, MidpointRounding.AwayFromZero),
                    line.Vat);
            }

            foreach (var amount in new[] { amounts.TotalNet, amounts.Discount, amounts.NetAfterDiscount, amounts.TotalVat, amounts.GrandTotal }
                         .Concat(amounts.Lines.SelectMany(line => new[] { line.Net, line.DiscountShare, line.Vat })))
            {
                Assert.True(Scale(amount) <= 2, $"{amount} has more than two decimals");
                Assert.True(amount >= 0m);
            }

            // Proportional, not just adding up: each share is within one halala of its exact proportional part (D x net / total net).
            if (amounts.TotalNet > 0m)
            {
                foreach (var line in amounts.Lines)
                {
                    Assert.True(
                        Math.Abs(line.DiscountShare * amounts.TotalNet - amounts.Discount * line.Net) < amounts.TotalNet * 0.01m,
                        "a share is not proportional to its line");
                }
            }

            // Deterministic: the same invoice always gives the same amounts.
            var again = InvoiceMoney.Calculate(lines, discount);
            Assert.Equal(amounts.Lines, again.Lines);
            Assert.Equal(
                [amounts.TotalNet, amounts.Discount, amounts.NetAfterDiscount, amounts.TotalVat, amounts.GrandTotal],
                [again.TotalNet, again.Discount, again.NetAfterDiscount, again.TotalVat, again.GrandTotal]);
        }
    }
}
