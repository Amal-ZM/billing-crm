using BillingCrm.Application.Auth;
using BillingCrm.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillingCrm.Infrastructure.Auth;

public sealed class JwtTokenService(JwtOptions options, TimeProvider time) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(options.SigningKeyBytes),
        SecurityAlgorithms.HmacSha256);

    public IssuedToken Issue(StaffUser user)
    {
        var issuedAt = time.GetUtcNow();
        var expires = issuedAt.AddMinutes(options.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email,
                ["role"] = user.Role.ToString(),
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = _credentials,
        };

        return new IssuedToken(_handler.CreateToken(descriptor), expires);
    }
}
