using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Customers;

/// <summary>A customer's details after trimming and validation.</summary>
public sealed record ValidCustomer(string Name, CustomerType Type, string Email, string Phone, string? VatNumber, string Address);

/// <summary>
/// The business rules for a customer's fields (spec FR-005). Trims first, reports every offending field
/// together, and keys the errors by camelCase field name so the UI can put each message on its field.
/// </summary>
public static class CustomerValidator
{
    public const int MaxName = 200;
    public const int MaxEmail = 256;
    public const int MinPhone = 7;
    public const int MaxPhone = 20;
    public const int MaxAddress = 500;
    public const int MaxVatNumber = 30;

    public static Result<ValidCustomer> Validate(CustomerInput input)
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

        var type = ParseType(Trim(input.Type), out var typeError);
        if (typeError is not null)
        {
            errors["type"] = [typeError];
        }

        var email = Trim(input.Email);
        if (email.Length == 0)
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > MaxEmail)
        {
            errors["email"] = [$"Email must be at most {MaxEmail} characters."];
        }
        else if (!IsEmailShaped(email))
        {
            errors["email"] = ["Email must be a valid email address."];
        }

        var phone = Trim(input.Phone);
        if (phone.Length == 0)
        {
            errors["phone"] = ["Phone is required."];
        }
        else if (phone.Length is < MinPhone or > MaxPhone)
        {
            errors["phone"] = [$"Phone must be {MinPhone} to {MaxPhone} characters."];
        }
        else if (!phone.All(IsPhoneCharacter))
        {
            errors["phone"] = ["Phone can only contain digits, spaces, +, - and parentheses."];
        }

        var address = Trim(input.Address);
        if (address.Length == 0)
        {
            errors["address"] = ["Address is required."];
        }
        else if (address.Length > MaxAddress)
        {
            errors["address"] = [$"Address must be at most {MaxAddress} characters."];
        }

        AppendGuardError(errors, "name", "Name", name);
        AppendGuardError(errors, "address", "Address", address);

        string? vatNumber = Trim(input.VatNumber);
        if (vatNumber.Length == 0)
        {
            vatNumber = null;
        }
        else if (vatNumber.Length > MaxVatNumber)
        {
            errors["vatNumber"] = [$"VAT number must be at most {MaxVatNumber} characters."];
        }
        else if (!vatNumber.All(IsVatCharacter))
        {
            errors["vatNumber"] = ["VAT number can only contain letters, digits, spaces and hyphens."];
        }

        return errors.Count > 0
            ? Result.Fail<ValidCustomer>(Error.Validation(errors))
            : Result.Success(new ValidCustomer(name, type!.Value, email, phone, vatNumber, address));
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

    private static CustomerType? ParseType(string value, out string? error)
    {
        error = null;
        if (value.Length == 0)
        {
            error = "Type is required.";
            return null;
        }

        if (TryParseType(value, out var type))
        {
            return type;
        }

        error = "Type must be Company or Individual.";
        return null;
    }

    /// <summary>Only the two names are accepted (in any letter case); numbers such as "1" are not. Shared with search.</summary>
    internal static bool TryParseType(string value, out CustomerType type)
    {
        foreach (var candidate in Enum.GetValues<CustomerType>())
        {
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    /// <summary>A deliberately small rule, not a full RFC parser: one @, text on both sides, a dotted domain, no spaces.</summary>
    private static bool IsEmailShaped(string email)
    {
        if (email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        var domain = email[(at + 1)..];
        return domain.Contains('.') && domain.Split('.').All(label => label.Length > 0);
    }

    private static bool IsPhoneCharacter(char c) => char.IsDigit(c) || c is ' ' or '+' or '-' or '(' or ')';

    private static bool IsVatCharacter(char c) => char.IsLetterOrDigit(c) || c is ' ' or '-';
}
