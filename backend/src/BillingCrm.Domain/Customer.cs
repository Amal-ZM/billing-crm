namespace BillingCrm.Domain;

/// <summary>
/// A person or company BillingCRM bills. There is no delete: a customer is retired by making it
/// inactive, because invoices will later refer to it.
/// </summary>
public class Customer
{
    // For EF Core.
    private Customer()
    {
        Name = string.Empty;
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
    }

    private Customer(
        string name,
        CustomerType type,
        string email,
        string phone,
        string? vatNumber,
        string address,
        DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        Name = name;
        Type = type;
        Email = email;
        NormalizedEmail = EmailNormalizer.Normalize(email);
        Phone = phone;
        VatNumber = vatNumber;
        Address = address;
        IsActive = true;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public CustomerType Type { get; private set; }

    /// <summary>As entered (trimmed); comparisons use <see cref="NormalizedEmail"/>.</summary>
    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public string Phone { get; private set; }

    public string? VatNumber { get; private set; }

    public string Address { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <remarks>Callers validate and trim first (CustomerValidator); a new customer is always active.</remarks>
    public static Customer Create(
        string name,
        CustomerType type,
        string email,
        string phone,
        string? vatNumber,
        string address,
        DateTime nowUtc) => new(name, type, email, phone, vatNumber, address, nowUtc);

    /// <summary>Applies new details. Returns true, and moves <see cref="UpdatedAtUtc"/>, only if something changed.</summary>
    public bool Update(
        string name,
        CustomerType type,
        string email,
        string phone,
        string? vatNumber,
        string address,
        DateTime nowUtc)
    {
        var normalizedEmail = EmailNormalizer.Normalize(email);
        var changed = Name != name
            || Type != type
            || Email != email
            || NormalizedEmail != normalizedEmail
            || Phone != phone
            || VatNumber != vatNumber
            || Address != address;

        if (!changed)
        {
            return false;
        }

        Name = name;
        Type = type;
        Email = email;
        NormalizedEmail = normalizedEmail;
        Phone = phone;
        VatNumber = vatNumber;
        Address = address;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Returns true, and moves <see cref="UpdatedAtUtc"/>, only if the state really changed.</summary>
    public bool SetActive(bool isActive, DateTime nowUtc)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
