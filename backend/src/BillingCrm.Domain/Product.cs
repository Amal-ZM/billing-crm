namespace BillingCrm.Domain;

/// <summary>
/// Something BillingCRM can bill for: a price-list entry, not a stocked good (there is no quantity on hand).
/// There is no delete: a product is retired by making it inactive, because invoices will later refer to it
/// and copy its price and VAT into their own lines.
/// </summary>
public class Product
{
    // For EF Core.
    private Product()
    {
        Name = string.Empty;
        Sku = string.Empty;
    }

    private Product(string name, string sku, ProductUnit unit, decimal unitPrice, decimal vatPercent, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        Name = name;
        Sku = sku;
        Unit = unit;
        UnitPrice = unitPrice;
        VatPercent = vatPercent;
        IsActive = true;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Stored already normalized (trimmed, upper-case), so comparing SKUs is comparing this value.</summary>
    public string Sku { get; private set; }

    public ProductUnit Unit { get; private set; }

    /// <summary>SAR, exact decimal, two decimal places at most.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>A percentage from 0 to 100 (15 means 15%), not an amount of money.</summary>
    public decimal VatPercent { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <remarks>Callers validate and normalize first (ProductValidator); a new product is always active.</remarks>
    public static Product Create(string name, string sku, ProductUnit unit, decimal unitPrice, decimal vatPercent, DateTime nowUtc) =>
        new(name, sku, unit, unitPrice, vatPercent, nowUtc);

    /// <summary>Applies new details. Returns true, and moves <see cref="UpdatedAtUtc"/>, only if something changed.</summary>
    public bool Update(string name, string sku, ProductUnit unit, decimal unitPrice, decimal vatPercent, DateTime nowUtc)
    {
        var changed = Name != name
            || Sku != sku
            || Unit != unit
            || UnitPrice != unitPrice
            || VatPercent != vatPercent;

        if (!changed)
        {
            return false;
        }

        Name = name;
        Sku = sku;
        Unit = unit;
        UnitPrice = unitPrice;
        VatPercent = vatPercent;
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
