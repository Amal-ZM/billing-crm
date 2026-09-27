using System.Numerics;

namespace BillingCrm.Domain;

/// <summary>One line as the calculator sees it: what was saved on the line (quantity, unit price and VAT percent), nothing else.</summary>
public sealed record AmountLine(decimal Quantity, decimal UnitPrice, decimal VatPercent);

/// <summary>The amounts of one line. <see cref="DiscountShare"/> is this line's part of the header discount; <see cref="Vat"/> is charged on <c>Net - DiscountShare</c>.</summary>
public sealed record LineAmounts(decimal Net, decimal DiscountShare, decimal Vat);

/// <summary>The amounts of a whole invoice, in Saudi riyals with exactly two decimals. <see cref="Lines"/> follows the order of the lines it was calculated from.</summary>
public sealed record InvoiceAmounts(
    IReadOnlyList<LineAmounts> Lines,
    decimal TotalNet,
    decimal Discount,
    decimal NetAfterDiscount,
    decimal TotalVat,
    decimal GrandTotal);

/// <summary>What has been paid on an invoice and what is left, in Saudi riyals with exactly two decimals (spec 008).</summary>
public sealed record InvoiceBalance(decimal Paid, decimal Remaining);

/// <summary>
/// The money rules of an invoice, in one place (spec 006; the project's <c>invoice-money</c> skill). While an invoice is a Draft nothing here is stored (it is frozen when the invoice leaves Draft, spec 007): the
/// amounts are calculated from what each line saved (quantity, unit price, VAT percent) and the invoice's discount every time
/// they are needed, so they can never disagree with the lines, and an invoice made before amounts existed shows them too.
/// <list type="bullet">
/// <item><b>Line net</b> = quantity x unit price, rounded to two decimals.</item>
/// <item><b>Header discount</b> comes off the <b>net</b>, <b>before VAT</b>: it is shared over the lines in proportion to their net,
/// and each line's <b>VAT is worked out on its own discounted net</b> (line net minus its share), so lines at different VAT
/// percentages are each charged correctly.</item>
/// <item><b>Line VAT</b> = (line net - its discount share) x VAT percent / 100, rounded to two decimals.</item>
/// <item><b>Grand total</b> = total net - discount + total VAT. Every total is a plain sum of amounts that were already rounded.</item>
/// </list>
/// Rounding is half away from zero (0.025 becomes 0.03, the way people round by hand; .NET's default would give 0.02), done once
/// per line for the net and once per line for the VAT, never on a running total. The discount is shared in whole halalas with
/// exact integer arithmetic (largest remainder, ties to the earlier line), so the shares add up to the discount to the last halala
/// and the same invoice always gives the same amounts. Only <see cref="decimal"/> is used: no <c>float</c> or <c>double</c>.
/// </summary>
public static class InvoiceMoney
{
    /// <summary>
    /// The largest line net, total net or grand total the system accepts (about ten trillion riyals). Far above any real invoice, and
    /// low enough that every amount is exact as a JSON number in a browser. The service refuses anything above it.
    /// </summary>
    public const decimal MaxAmount = 9_999_999_999_999.99m;

    /// <summary>
    /// What has been paid and what remains (spec 008): <b>paid</b> is the plain sum of the payments and <b>remaining</b> is the grand
    /// total minus that sum. Every payment has at most two decimals (the validator refuses a third, it never rounds), so a sum and a
    /// difference are exact and nothing is rounded here; adding 0.00 only keeps the figures at two decimals. Nothing is stored: the
    /// caller passes the invoice's own grand total (the frozen one once it has left Draft) and its payments each time. A negative
    /// remaining cannot arise through the application (an overpayment is refused), and if stored data ever showed one it is reported
    /// as it is, not hidden.
    /// </summary>
    public static InvoiceBalance Balance(decimal grandTotal, IEnumerable<decimal> paymentAmounts)
    {
        ArgumentNullException.ThrowIfNull(paymentAmounts);

        var paid = paymentAmounts.Sum() + 0.00m;
        return new InvoiceBalance(paid, grandTotal - paid + 0.00m);
    }

    /// <summary>
    /// Calculates the amounts for the lines and the discount. The caller has already checked that the discount is a number from 0 to
    /// the total net with at most two decimals (the service refuses anything else with a clear message), so a discount outside that
    /// is a programming error and throws.
    /// </summary>
    public static InvoiceAmounts Calculate(IReadOnlyList<AmountLine> lines, decimal discount)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var nets = lines.Select(line => Money(line.Quantity * line.UnitPrice)).ToArray();
        var totalNet = nets.Sum();

        if (discount < 0m || discount > totalNet || discount != decimal.Round(discount, 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(discount),
                discount,
                "A discount must be from 0 to the total net, with at most two decimals.");
        }

        var shares = ShareDiscount(nets, discount);

        var amounts = new List<LineAmounts>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            // VAT on the discounted net of THIS line, rounded once.
            var vat = Money((nets[i] - shares[i]) * lines[i].VatPercent / 100m);
            amounts.Add(new LineAmounts(nets[i], shares[i], vat));
        }

        var totalVat = amounts.Sum(line => line.Vat);
        var netAfterDiscount = totalNet - discount;

        return new InvoiceAmounts(
            amounts,
            Money(totalNet),
            Money(discount),
            Money(netAfterDiscount),
            Money(totalVat),
            Money(netAfterDiscount + totalVat));
    }

    /// <summary>Two decimals exactly (200 becomes 200.00), half away from zero.</summary>
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero) + 0.00m;

    /// <summary>
    /// Shares the discount over the lines in proportion to their net, in whole halalas: each line first gets the floor of its exact
    /// share, and the halalas left over (fewer than the number of lines) go one each to the lines with the largest remainders, ties
    /// to the earlier line. A line with no net has no remainder, so it never receives one, and no share exceeds its line's net.
    /// </summary>
    private static decimal[] ShareDiscount(decimal[] nets, decimal discount)
    {
        var shares = new decimal[nets.Length];
        var discountHalalas = ToHalalas(discount);
        var totalHalalas = ToHalalas(nets.Sum());
        if (discountHalalas.IsZero || totalHalalas.IsZero)
        {
            return shares.Select(_ => Money(0m)).ToArray();
        }

        var floors = new BigInteger[nets.Length];
        var remainders = new BigInteger[nets.Length];
        var given = BigInteger.Zero;
        for (var i = 0; i < nets.Length; i++)
        {
            var product = discountHalalas * ToHalalas(nets[i]);
            floors[i] = BigInteger.DivRem(product, totalHalalas, out remainders[i]);
            given += floors[i];
        }

        var leftOver = (int)(discountHalalas - given);
        var receivers = Enumerable.Range(0, nets.Length)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .Take(leftOver)
            .ToHashSet();

        for (var i = 0; i < nets.Length; i++)
        {
            var halalas = floors[i] + (receivers.Contains(i) ? BigInteger.One : BigInteger.Zero);
            shares[i] = Money((decimal)halalas / 100m);
        }

        return shares;
    }

    /// <summary>An amount with at most two decimals as a whole number of halalas (exact: nothing is lost).</summary>
    private static BigInteger ToHalalas(decimal amount) => new(amount * 100m);
}
