namespace BillingCrm.Domain;

public class StaffUser
{
    // For EF Core.
    private StaffUser()
    {
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        PasswordHash = string.Empty;
    }

    private StaffUser(string email, string passwordHash, StaffRole role)
    {
        Id = Guid.NewGuid();
        Email = email;
        NormalizedEmail = NormalizeEmail(email);
        PasswordHash = passwordHash;
        Role = role;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    /// <summary>One-way hash only. The plain password is never stored.</summary>
    public string PasswordHash { get; private set; }

    public StaffRole Role { get; private set; }

    /// <summary>Trim + upper-invariant, so email lookups ignore case and stray spaces.</summary>
    public static string NormalizeEmail(string email) => EmailNormalizer.Normalize(email);

    /// <remarks>
    /// Callers validate user input first and return a Result; the guards here only catch programming errors.
    /// </remarks>
    public static StaffUser Create(string email, string passwordHash, StaffRole role)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "Role must be Admin or Agent.");
        }

        return new StaffUser(email.Trim(), passwordHash, role);
    }
}
