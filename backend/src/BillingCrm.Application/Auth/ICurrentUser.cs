using BillingCrm.Domain;

namespace BillingCrm.Application.Auth;

/// <summary>The signed-in staff member who is making the request: their email address and role right now.</summary>
public sealed record Actor(string Email, StaffRole Role);

/// <summary>
/// Who is acting (spec 010). The invoice service reads this when it records a history entry, so the user of an entry can only ever be the
/// signed-in user: it is never a field of a request. Every invoice endpoint requires sign-in, so a missing user here is a defect and
/// <see cref="Current"/> throws instead of returning a guess (an entry without a user must never be written).
/// </summary>
public interface ICurrentUser
{
    /// <exception cref="InvalidOperationException">There is no signed-in user, or the sign-in carries no email or no known role.</exception>
    Actor Current { get; }
}
