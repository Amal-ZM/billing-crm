using System.Globalization;
using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>
/// One line after validation. It is <b>existing</b> (<see cref="ExistingId"/> set, only the quantity can change) or
/// <b>new</b> (<see cref="ProductId"/> set, to be copied from the product); never both.
/// </summary>
public sealed record ValidInvoiceLine(Guid? ExistingId, Guid? ProductId, decimal Quantity);

/// <summary>An invoice's request after trimming, parsing and validation. Whether the customer and products are active is the service's job.</summary>
public sealed record ValidInvoice(Guid CustomerId, DateOnly DueDate, string? Notes, decimal Discount, IReadOnlyList<ValidInvoiceLine> Lines);

/// <summary>
/// The shape rules for an invoice request (spec FR-003 to FR-010). It trims first, parses the ids and the date from text,
/// reports every problem together, and keys the errors by camelCase field name with array paths for lines
/// (<c>lines[0].quantity</c>) so the UI can put each message on its control. Money is checked, never adjusted: a quantity with
/// a third decimal place is refused, not rounded.
/// </summary>
public static class InvoiceValidator
{
    public const int MaxLines = 100;
    public const int MaxNotes = 1000;
    public const decimal MaxQuantity = 999_999.99m;

    private const string QuantityRule = "Quantity must be a number greater than zero with at most two decimal places.";
    private const string DiscountRule = "Discount must be a number of 0 or more with at most two decimal places.";
    private const string DiscountTooBig = "Discount cannot be more than 9,999,999,999,999.99.";

    /// <param name="today">Today's calendar date in Saudi Arabia (UTC+03:00).</param>
    public static Result<ValidInvoice> ForCreate(InvoiceInput input, DateOnly today) => ToResult(InspectForCreate(input, today));

    /// <param name="currentDueDate">The Draft's saved due date: an unchanged date is always accepted, even if it has since passed.</param>
    public static Result<ValidInvoice> ForEdit(InvoiceInput input, DateOnly today, DateOnly currentDueDate) =>
        ToResult(InspectForEdit(input, today, currentDueDate));

    public static Inspection InspectForCreate(InvoiceInput input, DateOnly today) => Inspect(input, today, null);

    public static Inspection InspectForEdit(InvoiceInput input, DateOnly today, DateOnly currentDueDate) => Inspect(input, today, currentDueDate);

    /// <summary>
    /// Everything the shape rules could read from a request, <b>even when some of it is wrong</b>: the ids that did parse are
    /// still there, so the service can also check that the customer and the products are active and report all problems in one answer.
    /// </summary>
    public sealed record Inspection(
        Guid? CustomerId,
        DateOnly? DueDate,
        string? Notes,
        decimal Discount,
        IReadOnlyList<ValidInvoiceLine> Lines,
        Dictionary<string, string[]> Errors);

    private static Result<ValidInvoice> ToResult(Inspection inspection) =>
        inspection.Errors.Count > 0
            ? Result.Fail<ValidInvoice>(Error.Validation(inspection.Errors))
            : Result.Success(new ValidInvoice(inspection.CustomerId!.Value, inspection.DueDate!.Value, inspection.Notes, inspection.Discount, inspection.Lines));

    private static Inspection Inspect(InvoiceInput input, DateOnly today, DateOnly? currentDueDate)
    {
        var editing = currentDueDate is not null;
        var errors = new Dictionary<string, string[]>();

        var customerId = ParseCustomer(input.CustomerId, errors);
        var dueDate = ParseDueDate(input.DueDate, today, currentDueDate, errors);
        var notes = ParseNotes(input.Notes, errors);
        var discount = ParseDiscount(input.Discount, errors);
        var lines = ParseLines(input.Lines, editing, errors);

        return new Inspection(customerId, dueDate, notes, discount, lines, errors);
    }

    private static Guid? ParseCustomer(string? text, Dictionary<string, string[]> errors)
    {
        var value = Trim(text);
        if (value.Length == 0)
        {
            errors["customerId"] = ["Choose a customer."];
            return null;
        }

        if (!Guid.TryParse(value, out var id))
        {
            errors["customerId"] = ["Customer is not valid."];
            return null;
        }

        return id;
    }

    private static DateOnly? ParseDueDate(string? text, DateOnly today, DateOnly? currentDueDate, Dictionary<string, string[]> errors)
    {
        var value = Trim(text);
        if (value.Length == 0)
        {
            errors["dueDate"] = ["Due date is required."];
            return null;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors["dueDate"] = ["Due date must be a date in the form yyyy-MM-dd."];
            return null;
        }

        // A Draft keeps a due date that has since passed as long as it is not changed.
        if (date < today && date != currentDueDate)
        {
            errors["dueDate"] = ["Due date cannot be in the past."];
            return null;
        }

        return date;
    }

    private static string? ParseNotes(string? text, Dictionary<string, string[]> errors)
    {
        var value = Trim(text);
        if (value.Length > MaxNotes)
        {
            // Fixed text (not formatted with the machine's culture), so the message is the same everywhere.
            errors["notes"] = ["Notes must be at most 1,000 characters."];
        }

        AppendGuardError(errors, "notes", "Notes", value);

        return value.Length == 0 ? null : value;
    }

    /// <summary>Adds a bidi/control-character refusal to <paramref name="field"/>'s errors (spec 012, FR-009), appending to any
    /// length-rule message already there rather than replacing it. Never runs on an empty value: notes are optional.</summary>
    private static void AppendGuardError(Dictionary<string, string[]> errors, string field, string label, string value)
    {
        if (value.Length == 0 || !FreeTextGuard.Refuses(value))
        {
            return;
        }

        var message = $"{label} contains a character that is not allowed.";
        errors[field] = errors.TryGetValue(field, out var existing) ? [.. existing, message] : [message];
    }

    /// <summary>
    /// The discount's own shape: none (null) is 0.00; otherwise 0 or more, at most two decimals (refused, never rounded, like a
    /// price), and no more than the amount ceiling. Whether it is more than the invoice's total net needs the lines, so the service checks that.
    /// </summary>
    private static decimal ParseDiscount(decimal? discount, Dictionary<string, string[]> errors)
    {
        if (discount is not { } value)
        {
            return 0.00m;
        }

        if (value < 0m || value != decimal.Round(value, 2))
        {
            errors["discount"] = [DiscountRule];
            return 0.00m;
        }

        if (value > InvoiceMoney.MaxAmount)
        {
            errors["discount"] = [DiscountTooBig];
            return 0.00m;
        }

        // Nothing can be lost here (checked above). Adding 0.00 pads to two places so 20.1 is saved and read back as 20.10.
        return decimal.Round(value, 2) + 0.00m;
    }

    private static List<ValidInvoiceLine> ParseLines(IReadOnlyList<InvoiceLineInput>? input, bool editing, Dictionary<string, string[]> errors)
    {
        var lines = new List<ValidInvoiceLine>();

        if (input is null || input.Count == 0)
        {
            errors["lines"] = ["An invoice needs at least one line."];
            return lines;
        }

        if (input.Count > MaxLines)
        {
            errors["lines"] = [$"An invoice can have at most {MaxLines} lines."];
            return lines;
        }

        var seenIds = new HashSet<Guid>();
        for (var i = 0; i < input.Count; i++)
        {
            var line = input[i];
            var prefix = $"lines[{i}]";
            var idText = Trim(line?.Id);
            var productText = Trim(line?.ProductId);

            Guid? existingId = null;
            Guid? productId = null;

            if (idText.Length > 0 && !editing)
            {
                errors[$"{prefix}.id"] = ["A new invoice cannot have existing lines."];
            }
            else if (idText.Length > 0 && productText.Length > 0)
            {
                errors[$"{prefix}.id"] = ["A line has either an id or a product, not both."];
            }
            else if (idText.Length > 0)
            {
                if (!Guid.TryParse(idText, out var parsedId))
                {
                    errors[$"{prefix}.id"] = ["Line id is not valid."];
                }
                else if (!seenIds.Add(parsedId))
                {
                    errors[$"{prefix}.id"] = ["Each line can appear only once."];
                }
                else
                {
                    existingId = parsedId;
                }
            }
            else if (productText.Length == 0)
            {
                errors[$"{prefix}.productId"] = ["Choose a product."];
            }
            else if (!Guid.TryParse(productText, out var parsedProduct))
            {
                errors[$"{prefix}.productId"] = ["Product is not valid."];
            }
            else
            {
                productId = parsedProduct;
            }

            var quantity = CheckQuantity(line?.Quantity, $"{prefix}.quantity", errors);
            lines.Add(new ValidInvoiceLine(existingId, productId, quantity ?? 0m));
        }

        return lines;
    }

    private static decimal? CheckQuantity(decimal? quantity, string key, Dictionary<string, string[]> errors)
    {
        if (quantity is not { } value || value <= 0)
        {
            errors[key] = [QuantityRule];
            return null;
        }

        if (value > MaxQuantity)
        {
            errors[key] = ["Quantity cannot be more than 999,999.99."];
            return null;
        }

        // Exact decimal arithmetic: 1.500 is the same value as 1.5 and is fine; 1.005 is not, and is refused rather than rounded.
        if (value != decimal.Round(value, 2))
        {
            errors[key] = [QuantityRule];
            return null;
        }

        // Nothing can be lost here (checked above). Round trims extra zeros and adding 0.00 pads to two places, so the
        // saved value has the same shape as a later read (2 becomes 2.00, 1.500 becomes 1.50).
        return decimal.Round(value, 2) + 0.00m;
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;
}
