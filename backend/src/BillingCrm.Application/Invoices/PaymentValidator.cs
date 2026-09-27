using System.Globalization;
using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>A payment's request after trimming, parsing and validation.</summary>
public sealed record ValidPayment(DateOnly PaidOn, decimal Amount, PaymentMethod Method, string? Note);

/// <summary>
/// The rules for the body of a payment (spec 008, contracts/payments-api.md). It trims first, parses the date from text, reports every
/// problem together keyed by camelCase field name (<c>date</c>, <c>amount</c>, <c>method</c>, <c>note</c>) so the UI can put each message
/// on its control, and never adjusts money: an amount with a third decimal place is refused, not rounded. It needs three facts about the
/// invoice, passed in by the service: today's date in Saudi Arabia, the Saudi date the invoice was sent, and what remains to be paid.
/// The invoice's <b>status</b> is not judged here: the service refuses a payment on an invoice that is not Sent before this runs.
/// </summary>
public static class PaymentValidator
{
    public const int MaxNote = InvoiceValidator.MaxNotes;

    public const string AmountRequired = "Amount is required.";
    public const string AmountRule = "Amount must be a number greater than zero with at most two decimal places.";
    public const string DateRequired = "Date is required.";
    public const string DateFormat = "Date must be a date in the form yyyy-MM-dd.";
    public const string DateInTheFuture = "Date cannot be in the future.";
    public const string MethodRequired = "Method is required.";
    public const string MethodRule = "Method must be Cash, Bank or Other.";
    public const string NoteTooLong = "Note must be at most 1,000 characters.";

    /// <param name="today">Today's calendar date in Saudi Arabia (UTC+03:00): a payment cannot be dated after it.</param>
    /// <param name="sentOn">The Saudi date the invoice was sent: a payment cannot be dated before it. Null skips the check.</param>
    /// <param name="remaining">What is left to pay: an amount above it is refused (no overpaying).</param>
    public static Result<ValidPayment> Validate(PaymentInput input, DateOnly today, DateOnly? sentOn, decimal remaining)
    {
        var errors = new Dictionary<string, string[]>();

        var paidOn = ParseDate(input.Date, today, sentOn, errors);
        var amount = ParseAmount(input.Amount, remaining, errors);
        var method = ParseMethod(input.Method, errors);
        var note = ParseNote(input.Note, errors);

        return errors.Count > 0
            ? Result.Fail<ValidPayment>(Error.Validation(errors))
            : Result.Success(new ValidPayment(paidOn!.Value, amount!.Value, method!.Value, note));
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;

    private static DateOnly? ParseDate(string? text, DateOnly today, DateOnly? sentOn, Dictionary<string, string[]> errors)
    {
        var value = Trim(text);
        if (value.Length == 0)
        {
            errors["date"] = [DateRequired];
            return null;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors["date"] = [DateFormat];
            return null;
        }

        if (date > today)
        {
            errors["date"] = [DateInTheFuture];
            return null;
        }

        if (sentOn is { } sent && date < sent)
        {
            errors["date"] = [$"Date cannot be before the invoice was sent ({sent.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})."];
            return null;
        }

        return date;
    }

    /// <summary>
    /// Greater than zero, at most two decimals (refused, never rounded, like a price), and not more than what remains. The overpayment
    /// message is only given for an amount that is otherwise well formed.
    /// </summary>
    private static decimal? ParseAmount(decimal? amount, decimal remaining, Dictionary<string, string[]> errors)
    {
        if (amount is not { } value)
        {
            errors["amount"] = [AmountRequired];
            return null;
        }

        if (value <= 0m || value != decimal.Round(value, 2))
        {
            errors["amount"] = [AmountRule];
            return null;
        }

        if (value > remaining)
        {
            // Fixed text and invariant culture, so the message is the same on every machine.
            errors["amount"] = [$"Amount cannot be more than the remaining {remaining.ToString("0.00", CultureInfo.InvariantCulture)} SAR."];
            return null;
        }

        // Nothing can be lost here (checked above). Adding 0.00 pads to two places so 100.1 is saved and read back as 100.10.
        return decimal.Round(value, 2) + 0.00m;
    }

    /// <summary>Exactly Cash, Bank or Other, spelled that way (stricter than a product's unit: a money record should not guess).</summary>
    private static PaymentMethod? ParseMethod(string? text, Dictionary<string, string[]> errors)
    {
        var value = Trim(text);
        if (value.Length == 0)
        {
            errors["method"] = [MethodRequired];
            return null;
        }

        foreach (var candidate in Enum.GetValues<PaymentMethod>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        errors["method"] = [MethodRule];
        return null;
    }

    private static string? ParseNote(string? text, Dictionary<string, string[]> errors)
    {
        var value = Trim(text);
        if (value.Length > MaxNote)
        {
            errors["note"] = [NoteTooLong];
        }

        AppendGuardError(errors, "note", "Note", value);

        return value.Length == 0 ? null : value;
    }

    /// <summary>Adds a bidi/control-character refusal to <paramref name="field"/>'s errors (spec 012, FR-009), appending to any
    /// length-rule message already there rather than replacing it. Never runs on an empty value: the note is optional.</summary>
    private static void AppendGuardError(Dictionary<string, string[]> errors, string field, string label, string value)
    {
        if (value.Length == 0 || !FreeTextGuard.Refuses(value))
        {
            return;
        }

        var message = $"{label} contains a character that is not allowed.";
        errors[field] = errors.TryGetValue(field, out var existing) ? [.. existing, message] : [message];
    }
}
