using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillingCrm.Api.Tests;

/// <summary>Mints tokens with the test signing key so tests can present valid, expired and tampered credentials.</summary>
public static class TestTokens
{
    public const string Issuer = "billingcrm-api";
    public const string Audience = "billingcrm-web";

    public static string Admin(DateTime? now = null) => Create("admin@crm.test", "Admin", now);

    public static string Agent(DateTime? now = null) => Create("agent@crm.test", "Agent", now);

    /// <summary>Signed correctly but both <c>nbf</c> and <c>exp</c> are in the past.</summary>
    public static string Expired(string role = "Admin") =>
        Create("expired@crm.test", role, DateTime.UtcNow.AddHours(-3), lifetime: TimeSpan.FromHours(1));

    /// <summary>
    /// A valid token whose signature has been altered. A character in the middle of the signature is
    /// changed: the last base64url character carries unused padding bits, and changing only those
    /// can leave the decoded signature identical.
    /// </summary>
    public static string Tampered()
    {
        var token = Admin();
        var signatureStart = token.LastIndexOf('.') + 1;
        var index = signatureStart + 10;
        var replacement = token[index] == 'A' ? 'B' : 'A';
        return token[..index] + replacement + token[(index + 1)..];
    }

    public static string WithIssuer(string issuer) =>
        Create("other@crm.test", "Admin", null, issuer: issuer);

    public static string Create(
        string email,
        string role,
        DateTime? now = null,
        TimeSpan? lifetime = null,
        string? issuer = null,
        string? signingKey = null)
    {
        var issuedAt = now ?? DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + (lifetime ?? TimeSpan.FromMinutes(30)),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["email"] = email,
                ["role"] = role,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? ApiFactory.TestSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
