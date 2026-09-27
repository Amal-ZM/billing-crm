using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Products;

/// <summary>A product's details after trimming, normalizing and validation.</summary>
public sealed record ValidProduct(string Name, string Sku, ProductUnit Unit, decimal UnitPrice, decimal VatPercent);

/// <summary>
/// The business rules for a product's fields (spec FR-006). Trims first, reports every offending field together,
/// and keys the errors by camelCase field name so the UI can put each message on its field. Money is checked,
/// never adjusted: a price with a third decimal place is refused, not rounded (spec FR-007).
/// </summary>
public static class ProductValidator
{
    public const int MaxName = 200;
    public const int MaxSku = 50;
    public const decimal MaxUnitPrice = 999_999_999.99m;
    public const decimal DefaultVatPercent = 15m;
    public const decimal MaxVatPercent = 100m;

    /// <param name="defaultVat">True when adding: an absent VAT percent becomes 15. False when editing: it is required.</param>
    public static Result<ValidProduct> Validate(ProductInput input, bool defaultVat)
    {
        var errors = new Dictionary<string, string[]>();

        var name = Trim(input.Name);
        if (name.Length == 0)
        {
            errors["name"] = ["Name is required."];
        }
        else if (name.Length > MaxName)
        {
            errors["name"] = [$"Name must be at most {MaxName} characters."];
        }

        AppendGuardError(errors, "name", "Name", name);

        var sku = Trim(input.Sku);
        if (sku.Length == 0)
        {
            errors["sku"] = ["SKU is required."];
        }
        else if (sku.Length > MaxSku)
        {
            errors["sku"] = [$"SKU must be at most {MaxSku} characters."];
        }
        else if (!sku.All(IsSkuCharacter))
        {
            errors["sku"] = ["SKU can only contain letters A-Z, digits, hyphens and underscores."];
        }
        else
        {
            // Safe now that only ASCII remains: no culture can change what upper-case means here.
            sku = sku.ToUpperInvariant();
        }

        var unit = ParseUnit(Trim(input.Unit), out var unitError);
        if (unitError is not null)
        {
            errors["unit"] = [unitError];
        }

        var unitPriceError = CheckUnitPrice(input.UnitPrice);
        if (unitPriceError is not null)
        {
            errors["unitPrice"] = [unitPriceError];
        }

        var vatPercent = input.VatPercent ?? (defaultVat ? DefaultVatPercent : null);
        var vatError = CheckVatPercent(vatPercent);
        if (vatError is not null)
        {
            errors["vatPercent"] = [vatError];
        }

        return errors.Count > 0
            ? Result.Fail<ValidProduct>(Error.Validation(errors))
            : Result.Success(new ValidProduct(name, sku, unit!.Value, ToTwoDecimals(input.UnitPrice!.Value), ToTwoDecimals(vatPercent!.Value)));
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>Adds a bidi/control-character refusal to <paramref name="field"/>'s errors (spec 012, FR-009), appending to any
    /// length-rule message already there rather than replacing it. Never runs on an already-empty value: the "is required" error
    /// covers that case on its own.</summary>
    private static void AppendGuardError(Dictionary<string, string[]> errors, string field, string label, string value)
    {
        if (value.Length == 0 || !FreeTextGuard.Refuses(value))
        {
            return;
        }

        var message = $"{label} contains a character that is not allowed.";
        errors[field] = errors.TryGetValue(field, out var existing) ? [.. existing, message] : [message];
    }

    private static ProductUnit? ParseUnit(string value, out string? error)
    {
        error = null;
        if (value.Length == 0)
        {
            error = "Unit is required.";
            return null;
        }

        if (TryParseUnit(value, out var unit))
        {
            return unit;
        }

        error = "Unit must be Hour, Item or Service.";
        return null;
    }

    /// <summary>Only the three names are accepted (in any letter case); numbers such as "1" are not. Shared with search.</summary>
    internal static bool TryParseUnit(string value, out ProductUnit unit)
    {
        foreach (var candidate in Enum.GetValues<ProductUnit>())
        {
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                unit = candidate;
                return true;
            }
        }

        unit = default;
        return false;
    }

    private static string? CheckUnitPrice(decimal? price)
    {
        if (price is not { } value)
        {
            return "Unit price is required.";
        }

        if (value < 0)
        {
            return "Unit price cannot be negative.";
        }

        if (value > MaxUnitPrice)
        {
            return "Unit price cannot be more than 999,999,999.99.";
        }

        return HasMoreThanTwoDecimals(value) ? "Unit price can have at most two decimal places." : null;
    }

    private static string? CheckVatPercent(decimal? vat)
    {
        if (vat is not { } value)
        {
            return "VAT percent is required.";
        }

        if (value is < 0 or > MaxVatPercent)
        {
            return "VAT percent must be between 0 and 100.";
        }

        return HasMoreThanTwoDecimals(value) ? "VAT percent can have at most two decimal places." : null;
    }

    /// <summary>
    /// Only ever called after the checks above proved there is nothing beyond two decimals, so no digit is lost.
    /// It just gives 100, 100.5 and 100.500 the same shape a later read has (100.00, 100.50, 100.50): Round trims
    /// extra zeros and adding 0.00 pads to two places.
    /// </summary>
    private static decimal ToTwoDecimals(decimal value) => decimal.Round(value, 2) + 0.00m;

    /// <summary>Exact decimal arithmetic: 100.500 is fine (same value as 100.50), 10.005 is not.</summary>
    private static bool HasMoreThanTwoDecimals(decimal value) => value != decimal.Round(value, 2);

    /// <summary>ASCII only on purpose: char.IsLetter would also admit Arabic and accented letters.</summary>
    private static bool IsSkuCharacter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
