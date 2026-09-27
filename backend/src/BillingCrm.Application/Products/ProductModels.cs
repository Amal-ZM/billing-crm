using BillingCrm.Domain;

namespace BillingCrm.Application.Products;

/// <summary>
/// The body of add and edit. Text is nullable because it comes straight from JSON; the two numbers bind to
/// <c>decimal?</c> so a typed value like 100.10 never passes through a floating-point number.
/// </summary>
public sealed record ProductInput(
    string? Name,
    string? Sku,
    string? Unit,
    decimal? UnitPrice,
    decimal? VatPercent);

/// <summary>The body of the activate/deactivate operation (the products twin of customers' request).</summary>
public sealed record ProductActiveRequest(bool? IsActive);

/// <summary>The list query exactly as it arrives in the query string; parsed and validated by the service.</summary>
public sealed record ProductQuery(string? Q, string? Unit, string? Status, string? Page, string? PageSize);

/// <summary>Which products a list may contain, by active state.</summary>
public enum ProductStatusFilter
{
    Active,
    Inactive,
    All,
}

/// <summary>A validated search: text is trimmed (null when blank), page and size are within range.</summary>
public sealed record ProductSearch(string? Text, ProductUnit? Unit, ProductStatusFilter Status, int Page, int PageSize);

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Sku,
    string Unit,
    decimal UnitPrice,
    decimal VatPercent,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ProductDto From(Product product) => new(
        product.Id,
        product.Name,
        product.Sku,
        product.Unit.ToString(),
        product.UnitPrice,
        product.VatPercent,
        product.IsActive,
        product.CreatedAtUtc,
        product.UpdatedAtUtc);
}

public sealed record ProductListItem(
    Guid Id,
    string Name,
    string Sku,
    string Unit,
    decimal UnitPrice,
    decimal VatPercent,
    bool IsActive)
{
    public static ProductListItem From(Product product) => new(
        product.Id,
        product.Name,
        product.Sku,
        product.Unit.ToString(),
        product.UnitPrice,
        product.VatPercent,
        product.IsActive);
}
