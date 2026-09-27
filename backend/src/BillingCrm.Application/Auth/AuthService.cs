using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Auth;

public sealed class AuthService(IStaffUserStore store, IPasswordHasher hasher, ITokenService tokens)
{
    /// <summary>Matches the <c>StaffUsers.Email</c> column.</summary>
    public const int MaxEmailLength = 256;

    public const int MaxPasswordLength = 128;

    /// <summary>
    /// The single failure for both a wrong password and an unknown email, so neither the message
    /// nor its content reveals which part was wrong (spec FR-003).
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "invalid_credentials",
        "Invalid credentials",
        "Email or password is incorrect.");

    public async Task<Result<SignInResponse>> SignInAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email is required."];
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["Password is required."];
        }

        // Caps stop an oversized value from being upper-cased, sent to SQL, or hashed at length.
        if (request.Email is { Length: > MaxEmailLength })
        {
            errors["email"] = [$"Email must be at most {MaxEmailLength} characters."];
        }

        if (request.Password is { Length: > MaxPasswordLength })
        {
            errors["password"] = [$"Password must be at most {MaxPasswordLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Result.Fail<SignInResponse>(Error.Validation(errors));
        }

        var user = await store.FindByNormalizedEmailAsync(StaffUser.NormalizeEmail(request.Email!), cancellationToken);
        if (user is null)
        {
            // Spend the same time as a real verification so response time does not reveal unknown emails.
            hasher.Verify(hasher.DummyHash, request.Password!);
            return Result.Fail<SignInResponse>(InvalidCredentials);
        }

        if (!hasher.Verify(user.PasswordHash, request.Password!))
        {
            return Result.Fail<SignInResponse>(InvalidCredentials);
        }

        var token = tokens.Issue(user);
        return Result.Success(new SignInResponse(token.AccessToken, token.ExpiresAtUtc, user.Email, user.Role.ToString()));
    }
}
