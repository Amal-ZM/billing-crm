namespace BillingCrm.Domain;

/// <summary>
/// One row of an invoice. The product's name, SKU, unit, unit price and VAT percent are copied here <b>once</b>, when
/// the line is created, and never rewritten: whatever later happens to the product, the invoice keeps showing what it was
/// created with. Only the quantity can change, and only while the invoice is a Draft. A Draft calculates the net, discount share and VAT from these saved values (see InvoiceMoney); when the invoice leaves Draft they are frozen into the Frozen* columns.
/// </summary>
public class InvoiceLine
{
    // For EF Core.
    private InvoiceLine()
    {
        ProductName = string.Empty;
        ProductSku = string.Empty;
    }

    private InvoiceLine(Product product, int position, decimal quantity)
    {
        Id = Guid.NewGuid();
        Position = position;
        ProductId = product.Id;
        ProductName = product.Name;
        ProductSku = product.Sku;
        Unit = product.Unit;
        UnitPrice = product.UnitPrice;
        VatPercent = product.VatPercent;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }

    /// <summary>Set when the line is added to an invoice.</summary>
    public Guid InvoiceId { get; private set; }

    /// <summary>The order the lines were added in (1-based); never renumbered when a line is removed.</summary>
    public int Position { get; private set; }

    /// <summary>A reference only. Nothing reads the live product to show a line.</summary>
    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; }

    public string ProductSku { get; private set; }

    public ProductUnit Unit { get; private set; }

    /// <summary>SAR, exact decimal, copied from the product when the line was created.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>A percentage from 0 to 100, copied from the product when the line was created.</summary>
    public decimal VatPercent { get; private set; }

    /// <summary>Greater than zero, at most 999,999.99, two decimals at most. The only editable value.</summary>
    public decimal Quantity { get; private set; }

    /// <remarks>Reached only through <see cref="Invoice.AddLine"/>. Callers validate the quantity first (InvoiceValidator) and check the product is active.</remarks>
    internal static InvoiceLine CreateFrom(Product product, int position, decimal quantity) => new(product, position, quantity);

    /// <summary>
    /// The line's amounts as they were when its invoice left Draft (null while it is a Draft). Written once by the invoice
    /// (<see cref="Invoice.Send"/> or <see cref="Invoice.Cancel"/>) and read instead of calculating from then on.
    /// </summary>
    public decimal? FrozenNet { get; private set; }

    public decimal? FrozenDiscountShare { get; private set; }

    public decimal? FrozenVat { get; private set; }

    internal void AttachTo(Guid invoiceId) => InvoiceId = invoiceId;

    internal void Freeze(LineAmounts amounts)
    {
        FrozenNet = amounts.Net;
        FrozenDiscountShare = amounts.DiscountShare;
        FrozenVat = amounts.Vat;
    }

    /// <summary>
    /// Returns true only if the quantity really changed. Internal on purpose: it is reached only through
    /// <see cref="Invoice.SetLineQuantity"/>, which also moves the invoice's changed time. It does not judge the value: the
    /// InvoiceValidator is the one gate (greater than zero, at most 999,999.99, two decimals at most).
    /// </summary>
    internal bool SetQuantity(decimal quantity)
    {
        if (Quantity == quantity)
        {
            return false;
        }

        Quantity = quantity;
        return true;
    }
}
