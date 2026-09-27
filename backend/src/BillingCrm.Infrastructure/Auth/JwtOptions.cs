using System.Text;

namespace BillingCrm.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string SigningKeySetting = "Jwt:SigningKey";
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Rejects obviously low-entropy keys such as a repeated character.</summary>
    public const int MinimumDistinctKeyCharacters = 10;

    /// <summary>Secret. Supplied via user-secrets or the Jwt__SigningKey environment variable; never committed.</summary>
    public string? SigningKey { get; set; }

    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    public int ExpiryMinutes { get; set; } = 60;

    public byte[] SigningKeyBytes => Encoding.UTF8.GetBytes(SigningKey ?? string.Empty);

    /// <summary>
    /// Fails fast at start-up. Messages name the setting only; they never include a value.
    /// A misconfigured service is an unexpected failure, so this throws (Constitution III).
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException(
                $"Required setting '{SigningKeySetting}' is missing. " +
                "Supply it via user-secrets or the Jwt__SigningKey environment variable; " +
                "see the README section 'Configure the signing key'.");
        }

        if (SigningKeyBytes.Length < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Setting '{SigningKeySetting}' must be at least {MinimumSigningKeyBytes} bytes long.");
        }

        if (SigningKey.Distinct().Count() < MinimumDistinctKeyCharacters)
        {
            throw new InvalidOperationException(
                $"Setting '{SigningKeySetting}' is not random enough. Generate one as described in the README section 'Configure the signing key'.");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Required setting 'Jwt:Issuer' is missing.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Required setting 'Jwt:Audience' is missing.");
        }

        if (ExpiryMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Setting 'Jwt:ExpiryMinutes' must be between 1 and 1440.");
        }
    }
}
