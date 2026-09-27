using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The amounts of an invoice aggregate: they come from what its lines saved and its discount, and from nothing else.</summary>
public class InvoiceAmountsTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(1);
    private static readonly DateOnly Due = new(2030, 7, 1);

    private static Product Consulting() => Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100.00m, 15m, Now);

    private static Invoice DraftWith(Product product, decimal quantity, decimal discount = 0m)
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, null, Now, discount);
        invoice.AddLine(product, quantity, Now);
        return invoice;
    }

    [Fact]
    public void A_new_Draft_has_no_discount_by_default()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Due, null, Now);

        Assert.Equal(0m, invoice.Discount);
    }

    [Fact]
    public void Two_at_100_with_15_percent_VAT_is_200_30_230()
    {
        var amounts = DraftWith(Consulting(), 2m).Amounts;

        Assert.Equal(200.00m, amounts.TotalNet);
        Assert.Equal(30.00m, amounts.TotalVat);
        Assert.Equal(230.00m, amounts.GrandTotal);
        Assert.Equal(200.00m, amounts.Lines.Single().Net);
        Assert.Equal(30.00m, amounts.Lines.Single().Vat);
    }

    [Fact]
    public void The_discount_is_taken_off_before_VAT()
    {
        var amounts = DraftWith(Consulting(), 2m, 20.00m).Amounts;

        Assert.Equal(20.00m, amounts.Discount);
        Assert.Equal(180.00m, amounts.NetAfterDiscount);
        Assert.Equal(27.00m, amounts.TotalVat);
        Assert.Equal(207.00m, amounts.GrandTotal);
    }

    [Fact]
    public void The_amounts_use_the_price_saved_on_the_line_and_never_the_product_it_was_copied_from()
    {
        var product = Consulting();
        var invoice = DraftWith(product, 2m);

        product.Update("Consulting hour (renamed)", "CNS-001", ProductUnit.Hour, 150.00m, 5m, Later);
        product.SetActive(false, Later);

        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
    }

    [Fact]
    public void Changing_a_quantity_recalculates_from_the_saved_price()
    {
        var product = Consulting();
        var invoice = DraftWith(product, 2m);
        product.Update("Consulting hour", "CNS-001", ProductUnit.Hour, 150.00m, 5m, Later);

        Assert.True(invoice.SetLineQuantity(invoice.Lines.Single().Id, 3m, Later));

        Assert.Equal(300.00m, invoice.Amounts.TotalNet);
        Assert.Equal(45.00m, invoice.Amounts.TotalVat);
        Assert.Equal(345.00m, invoice.Amounts.GrandTotal);
    }

    [Fact]
    public void The_lines_are_calculated_in_the_order_they_were_added()
    {
        var invoice = DraftWith(Consulting(), 2m);
        invoice.AddLine(Product.Create("Website build", "WEB-9", ProductUnit.Service, 50.00m, 0m, Now), 1m, Now);

        Assert.Equal([200.00m, 50.00m], invoice.Amounts.Lines.Select(line => line.Net));
        Assert.Equal([30.00m, 0.00m], invoice.Amounts.Lines.Select(line => line.Vat));
    }

    [Fact]
    public void UpdateDetails_with_the_same_values_including_the_discount_changes_nothing()
    {
        var invoice = DraftWith(Consulting(), 2m, 20.00m);

        Assert.False(invoice.UpdateDetails(invoice.CustomerId, Due, null, 20.00m, Later));

        Assert.Equal(Now, invoice.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_with_a_different_discount_changes_it_and_moves_the_changed_time()
    {
        var invoice = DraftWith(Consulting(), 2m, 20.00m);

        Assert.True(invoice.UpdateDetails(invoice.CustomerId, Due, null, 0m, Later));

        Assert.Equal(0m, invoice.Discount);
        Assert.Equal(Later, invoice.UpdatedAtUtc);
        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
    }

    // ---- reading must never fail (review finding B1) ----

    [Fact]
    public void A_saved_discount_above_the_net_left_by_an_older_version_is_shown_as_the_whole_net_and_never_throws()
    {
        // Net 200.00 with a discount of 50.00 saved, then another person lowers the quantity so the net is 30.00 (a different row,
        // so nothing clashed): the stored discount is now above the net.
        var product = Consulting();
        var invoice = DraftWith(product, 2m, 50.00m);
        invoice.SetLineQuantity(invoice.Lines.Single().Id, 0.30m, Later);

        var amounts = invoice.Amounts;

        Assert.Equal(30.00m, amounts.TotalNet);
        Assert.Equal(30.00m, amounts.Discount);
        Assert.Equal(0.00m, amounts.NetAfterDiscount);
        Assert.Equal(0.00m, amounts.TotalVat);
        Assert.Equal(0.00m, amounts.GrandTotal);
        Assert.Equal(50.00m, invoice.Discount);
    }

}
