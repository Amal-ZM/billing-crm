using BillingCrm.Domain;

namespace BillingCrm.Application.Customers;

/// <summary>The body of add and edit. Everything is nullable because it comes straight from JSON.</summary>
public sealed record CustomerInput(
    string? Name,
    string? Type,
    string? Email,
    string? Phone,
    string? VatNumber,
    string? Address);

/// <summary>The body of the activate/deactivate operation.</summary>
public sealed record SetActiveRequest(bool? IsActive);

/// <summary>The list query exactly as it arrives in the query string; parsed and validated by the service.</summary>
public sealed record CustomerQuery(string? Q, string? Type, string? Active, string? Page, string? PageSize);

/// <summary>A validated search: text is trimmed (null when blank), page and size are within range.</summary>
public sealed record CustomerSearch(string? Text, CustomerType? Type, bool? IsActive, int Page, int PageSize);

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string Type,
    string Email,
    string Phone,
    string? VatNumber,
    string Address,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static CustomerDto From(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Type.ToString(),
        customer.Email,
        customer.Phone,
        customer.VatNumber,
        customer.Address,
        customer.IsActive,
        customer.CreatedAtUtc,
        customer.UpdatedAtUtc);
}

public sealed record CustomerListItem(
    Guid Id,
    string Name,
    string Type,
    string Email,
    string Phone,
    string? VatNumber,
    bool IsActive)
{
    public static CustomerListItem From(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Type.ToString(),
        customer.Email,
        customer.Phone,
        customer.VatNumber,
        customer.IsActive);
}
