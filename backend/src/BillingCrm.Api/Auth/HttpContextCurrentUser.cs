using BillingCrm.Application.Auth;
using BillingCrm.Domain;

namespace BillingCrm.Api.Auth;

/// <summary>
/// Reads who is acting from the signed-in user's token claims (<c>email</c> and <c>role</c>) and from nothing else: not a header, not the
/// query string, not the body. Every invoice endpoint requires sign-in, so a request without a usable user here is a defect and this throws
/// (with fixed text that never echoes a token value) instead of guessing.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Actor Current
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                throw new InvalidOperationException("There is no signed-in user for this request.");
            }

            var email = user.FindFirst("email")?.Value;
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("The signed-in user has no email address.");
            }

            var role = user.FindFirst("role")?.Value;
            if (role is null || !Enum.TryParse(role, ignoreCase: false, out StaffRole parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != role)
            {
                throw new InvalidOperationException("The signed-in user has no known role.");
            }

            return new Actor(email, parsed);
        }
    }
}
