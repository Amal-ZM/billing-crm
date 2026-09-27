namespace BillingCrm.Domain;

/// <summary>
/// One rule for comparing email addresses everywhere (staff users and customers): trim, then
/// upper-case invariant. Independent of any database collation.
/// </summary>
public static class EmailNormalizer
{
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
