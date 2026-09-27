namespace BillingCrm.Application.Auth;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);

    /// <summary>
    /// A valid hash of a random value nobody knows. Verifying against it costs the same as a real
    /// verification, so an unknown email cannot be told apart by response time.
    /// </summary>
    string DummyHash { get; }
}
