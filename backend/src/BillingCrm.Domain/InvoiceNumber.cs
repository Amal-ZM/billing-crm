using System.Globalization;

namespace BillingCrm.Domain;

/// <summary>
/// The one place that turns an invoice's number into text and back. The database stores only the integer
/// (drawn from a sequence), so <c>INV-000123</c> exists nowhere except as this formatting of 123.
/// </summary>
public static class InvoiceNumber
{
    public const string Prefix = "INV-";
    public const int Digits = 6;

    /// <summary>The highest number the six-digit form can show (and the sequence's maximum).</summary>
    public const int Max = 999_999;

    public static string Format(int number) =>
        Prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');

    /// <summary>Accepts exactly <c>INV-</c> followed by six digits (upper-case prefix, ASCII digits, no spaces).</summary>
    public static bool TryParse(string? text, out int number)
    {
        number = 0;
        if (text is null
            || text.Length != Prefix.Length + Digits
            || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = text.AsSpan(Prefix.Length);
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
