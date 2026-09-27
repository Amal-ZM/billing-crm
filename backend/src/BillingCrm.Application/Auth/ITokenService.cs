using BillingCrm.Domain;

namespace BillingCrm.Application.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

public interface ITokenService
{
    IssuedToken Issue(StaffUser user);
}
