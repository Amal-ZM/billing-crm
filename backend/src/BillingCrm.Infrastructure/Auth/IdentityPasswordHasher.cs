using BillingCrm.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace BillingCrm.Infrastructure.Auth;

/// <summary>Wraps ASP.NET Core Identity's salted, iterated, versioned PBKDF2 hasher.</summary>
public sealed class IdentityPasswordHasher : Application.Auth.IPasswordHasher
{
    // The Identity hasher ignores the user object; a shared placeholder keeps the generic simple.
    private static readonly object NoUser = new();

    private readonly PasswordHasher<object> _inner = new();

    public IdentityPasswordHasher()
    {
        // Built eagerly so the first unknown-email sign-in costs the same as every later one.
        DummyHash = Hash(Guid.NewGuid().ToString("N"));
    }

    public string DummyHash { get; }

    public string Hash(string password) => _inner.HashPassword(NoUser, password);

    public bool Verify(string passwordHash, string password) =>
        _inner.VerifyHashedPassword(NoUser, passwordHash, password) != PasswordVerificationResult.Failed;
}
