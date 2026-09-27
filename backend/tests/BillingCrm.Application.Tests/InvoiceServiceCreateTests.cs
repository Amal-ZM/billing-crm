using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class InvoiceServiceCreateTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly FakeCurrentUser _user = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Product _consulting;

    public InvoiceServiceCreateTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);

        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now.UtcDateTime);
        _products.Seed(_consulting);
    }

    private static string DueDate => "2030-07-01";

    private InvoiceInput Input(Guid? customerId = null, params InvoiceLineInput[] lines) =>
        new((customerId ?? _abc.Id).ToString(), DueDate, "Net 30", lines.Length == 0 ? [Line(_consulting, 2m)] : lines);

    private static InvoiceLineInput Line(Product product, decimal quantity) => new(null, product.Id.ToString(), quantity);

    private Product SeedProduct(string sku, string name, decimal price = 10m, decimal vat = 15m, bool active = true)
    {
        var product = Product.Create(name, sku, ProductUnit.Item, price, vat, Now.UtcDateTime);
        if (!active)
        {
            product.SetActive(false, Now.UtcDateTime);
        }

        _products.Seed(product);
        return product;
    }

    private static IReadOnlyDictionary<string, string[]> ErrorsOf(Result<InvoiceDto> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    // ---- the amounts (spec 006, story 1) ----

    [Fact]
    public async Task Two_at_100_with_15_percent_VAT_and_no_discount_is_net_200_VAT_30_grand_230()
    {
        var result = await _sut.CreateAsync(Input(), CancellationToken.None);

        var invoice = result.Value;
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(200.00m, line.Net);
        Assert.Equal(0.00m, line.DiscountShare);
        Assert.Equal(30.00m, line.Vat);
        Assert.Equal(0.00m, invoice.Discount);
        Assert.Equal(200.00m, invoice.Totals.TotalNet);
        Assert.Equal(0.00m, invoice.Totals.Discount);
        Assert.Equal(200.00m, invoice.Totals.NetAfterDiscount);
        Assert.Equal(30.00m, invoice.Totals.TotalVat);
        Assert.Equal(230.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task Lines_at_different_VAT_percentages_each_use_their_own_percentage()
    {
        var express = SeedProduct("EXP-1", "Express fee", 50m, 0m);

        var result = await _sut.CreateAsync(Input(null, Line(_consulting, 2m), Line(express, 1m)), CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal([200.00m, 50.00m], invoice.Lines.Select(line => line.Net));
        Assert.Equal([30.00m, 0.00m], invoice.Lines.Select(line => line.Vat));
        Assert.Equal(250.00m, invoice.Totals.TotalNet);
        Assert.Equal(30.00m, invoice.Totals.TotalVat);
        Assert.Equal(280.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_net_with_four_decimals_is_rounded_once_and_the_VAT_is_worked_out_on_the_rounded_net()
    {
        var pricey = SeedProduct("PR-1", "Odd price", 10.55m, 15m);

        var result = await _sut.CreateAsync(Input(null, Line(pricey, 1.25m)), CancellationToken.None);

        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(13.19m, line.Net);
        Assert.Equal(1.98m, line.Vat);
        Assert.Equal(15.17m, result.Value.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_free_item_has_zero_amounts_and_does_not_disturb_the_other_lines()
    {
        var free = SeedProduct("FREE-1", "Free sample", 0m, 15m);

        var result = await _sut.CreateAsync(Input(null, Line(free, 1m), Line(_consulting, 2m)), CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal([0.00m, 200.00m], invoice.Lines.Select(line => line.Net));
        Assert.Equal([0.00m, 30.00m], invoice.Lines.Select(line => line.Vat));
        Assert.Equal(230.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task Opening_the_invoice_shows_the_same_amounts_as_saving_it_did()
    {
        var created = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;

        var opened = (await _sut.GetAsync(created.Id, CancellationToken.None)).Value;

        Assert.Equal(created.Totals, opened.Totals);
        Assert.Equal(created.Lines.Select(line => (line.Net, line.DiscountShare, line.Vat)), opened.Lines.Select(line => (line.Net, line.DiscountShare, line.Vat)));
    }

    [Fact]
    public async Task The_amounts_of_a_response_add_up()
    {
        var express = SeedProduct("EXP-1", "Express fee", 49.99m, 5m);
        var invoice = (await _sut.CreateAsync(Input(null, Line(_consulting, 3.33m), Line(express, 7m)), CancellationToken.None)).Value;

        Assert.Equal(invoice.Lines.Sum(line => line.Net), invoice.Totals.TotalNet);
        Assert.Equal(invoice.Lines.Sum(line => line.DiscountShare), invoice.Totals.Discount);
        Assert.Equal(invoice.Lines.Sum(line => line.Vat), invoice.Totals.TotalVat);
        Assert.Equal(invoice.Totals.TotalNet - invoice.Totals.Discount, invoice.Totals.NetAfterDiscount);
        Assert.Equal(invoice.Totals.NetAfterDiscount + invoice.Totals.TotalVat, invoice.Totals.GrandTotal);
    }

    // ---- the discount and the limits (spec 006, story 2) ----

    [Fact]
    public async Task A_discount_of_20_on_a_net_of_200_at_15_percent_gives_180_27_207()
    {
        var result = await _sut.CreateAsync(Input() with { Discount = 20.00m }, CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal(20.00m, invoice.Discount);
        Assert.Equal(20.00m, invoice.Lines[0].DiscountShare);
        Assert.Equal(27.00m, invoice.Lines[0].Vat);
        Assert.Equal(200.00m, invoice.Totals.TotalNet);
        Assert.Equal(20.00m, invoice.Totals.Discount);
        Assert.Equal(180.00m, invoice.Totals.NetAfterDiscount);
        Assert.Equal(27.00m, invoice.Totals.TotalVat);
        Assert.Equal(207.00m, invoice.Totals.GrandTotal);
        Assert.Equal(20.00m, Assert.Single(_invoices.All).Discount);
    }

    [Fact]
    public async Task The_discount_is_shared_over_lines_with_different_VAT_and_each_line_pays_VAT_on_its_own_reduced_net()
    {
        var express = SeedProduct("EXP-1", "Express fee", 50m, 0m);
        var consulting = SeedProduct("CNS-2", "Consulting unit", 100m, 15m);

        var result = await _sut.CreateAsync(Input(null, Line(consulting, 1m), Line(express, 1m)) with { Discount = 30.00m }, CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal([20.00m, 10.00m], invoice.Lines.Select(line => line.DiscountShare));
        Assert.Equal([12.00m, 0.00m], invoice.Lines.Select(line => line.Vat));
        Assert.Equal(120.00m, invoice.Totals.NetAfterDiscount);
        Assert.Equal(132.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_discount_equal_to_the_whole_net_is_accepted_and_leaves_nothing_to_pay()
    {
        var result = await _sut.CreateAsync(Input() with { Discount = 200.00m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.00m, result.Value.Totals.NetAfterDiscount);
        Assert.Equal(0.00m, result.Value.Totals.TotalVat);
        Assert.Equal(0.00m, result.Value.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_discount_above_the_total_net_is_refused_on_the_discount_and_nothing_is_created()
    {
        var result = await _sut.CreateAsync(Input() with { Discount = 200.01m }, CancellationToken.None);

        var errors = ErrorsOf(result);
        Assert.Equal(["Discount cannot be more than the total net of the lines."], errors["discount"]);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task A_negative_discount_is_refused_on_the_discount_and_nothing_is_created()
    {
        var result = await _sut.CreateAsync(Input() with { Discount = -1m }, CancellationToken.None);

        Assert.True(ErrorsOf(result).ContainsKey("discount"));
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task A_line_whose_net_is_above_the_ceiling_is_refused_on_that_lines_quantity()
    {
        var huge = SeedProduct("HUGE-1", "Huge price", 999_999_999.99m, 15m);

        var result = await _sut.CreateAsync(Input(null, Line(_consulting, 1m), Line(huge, 999_999.99m)), CancellationToken.None);

        var errors = ErrorsOf(result);
        Assert.Equal(["lines[1].quantity"], errors.Keys);
        Assert.Equal(["The amount of this line is too large. Reduce the quantity."], errors["lines[1].quantity"]);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task Lines_that_are_each_allowed_but_add_up_above_the_ceiling_are_refused_on_the_lines()
    {
        var big = SeedProduct("BIG-1", "Big price", 999_999_999.99m, 0m);

        var result = await _sut.CreateAsync(Input(null, Line(big, 9_000m), Line(big, 9_000m)), CancellationToken.None);

        var errors = ErrorsOf(result);
        Assert.Equal(["lines"], errors.Keys);
        Assert.Equal(["The invoice total is too large. Reduce the quantities or remove lines."], errors["lines"]);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task A_line_of_exactly_the_ceiling_is_accepted_and_one_halala_more_is_refused()
    {
        var atCeiling = SeedProduct("MAX-1", "At the ceiling", 9_999_999_999_999.99m, 0m);
        var above = SeedProduct("MAX-2", "One halala over", 10_000_000_000_000.00m, 0m);

        var accepted = await _sut.CreateAsync(Input(null, Line(atCeiling, 1m)), CancellationToken.None);
        var refused = await _sut.CreateAsync(Input(null, Line(above, 1m)), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(9_999_999_999_999.99m, accepted.Value.Totals.GrandTotal);
        Assert.Equal(["The amount of this line is too large. Reduce the quantity."], ErrorsOf(refused)["lines[0].quantity"]);
    }

    [Fact]
    public async Task A_too_large_total_and_a_discount_above_the_net_are_reported_together()
    {
        var big = SeedProduct("BIG-3", "Big price 15", 999_999_999.99m, 15m);

        // Net 9,499,999,999,905.00 is allowed but with 15% VAT the grand total is not; the discount is above that net.
        var result = await _sut.CreateAsync(Input(null, Line(big, 9_500m)) with { Discount = 9_999_999_999_999.99m }, CancellationToken.None);

        var errors = ErrorsOf(result);
        Assert.Equal(["discount", "lines"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(["Discount cannot be more than the total net of the lines."], errors["discount"]);
        Assert.Equal(["The invoice total is too large. Reduce the quantities or remove lines."], errors["lines"]);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task The_size_checks_are_skipped_while_another_error_exists_and_reported_once_it_is_fixed()
    {
        var dormant = SeedProduct("OLD-2", "Retired", 10m, 15m, active: false);
        var huge = SeedProduct("HUGE-3", "Huge price", 999_999_999.99m, 0m);

        var first = await _sut.CreateAsync(Input(null, Line(dormant, 1m), Line(huge, 999_999.99m)), CancellationToken.None);
        var second = await _sut.CreateAsync(Input(null, Line(_consulting, 1m), Line(huge, 999_999.99m)), CancellationToken.None);

        Assert.Equal(["lines[0].productId"], ErrorsOf(first).Keys);
        Assert.Equal(["lines[1].quantity"], ErrorsOf(second).Keys);
    }

    [Fact]
    public async Task A_discount_above_the_ceiling_is_refused_on_the_discount_by_the_service()
    {
        var result = await _sut.CreateAsync(Input() with { Discount = 10_000_000_000_000m }, CancellationToken.None);

        Assert.Equal(["Discount cannot be more than 9,999,999,999,999.99."], ErrorsOf(result)["discount"]);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task A_grand_total_above_the_ceiling_because_of_VAT_alone_is_refused()
    {
        var big = SeedProduct("BIG-2", "Big price 15", 999_999_999.99m, 15m);

        // Net 9,499,999,999,905.00 is below the ceiling, but with 15% VAT the grand total is 10,924,999,999,890.75.
        var result = await _sut.CreateAsync(Input(null, Line(big, 9_500m)), CancellationToken.None);

        Assert.Equal(["The invoice total is too large. Reduce the quantities or remove lines."], ErrorsOf(result)["lines"]);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task When_other_errors_exist_the_discount_above_net_check_is_skipped()
    {
        var dormant = SeedProduct("OLD-1", "Retired", 10m, 15m, active: false);

        var result = await _sut.CreateAsync(Input(null, Line(dormant, 1m)) with { Discount = 1_000_000m }, CancellationToken.None);

        var errors = ErrorsOf(result);
        Assert.Equal(["lines[0].productId"], errors.Keys);
    }

    // ---- the acceptance scenario ----

    [Fact]
    public async Task Customer_ABC_and_CNS_001_with_quantity_2_is_saved_as_a_Draft_with_one_line_of_quantity_2()
    {
        var result = await _sut.CreateAsync(Input(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("Draft", invoice.Status);
        Assert.Equal("INV-000001", invoice.Number);
        Assert.Equal("ABC", invoice.Customer.Name);
        Assert.True(invoice.Customer.IsActive);
        Assert.Equal("2030-07-01", invoice.DueDate);
        Assert.Equal("Net 30", invoice.Notes);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(1, line.Position);
        Assert.Equal(1, _invoices.AddCalls);
    }

    [Fact]
    public async Task The_line_carries_a_copy_of_the_products_name_sku_unit_price_and_vat()
    {
        var result = await _sut.CreateAsync(Input(), CancellationToken.None);

        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(_consulting.Id, line.ProductId);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal("CNS-001", line.Sku);
        Assert.Equal("Hour", line.Unit);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);
    }

    [Fact]
    public async Task A_later_change_to_the_product_does_not_change_the_saved_line()
    {
        var created = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;

        _consulting.Update("Consulting day", "CNS-002", ProductUnit.Service, 999.99m, 0m, Now.UtcDateTime.AddDays(1));
        _consulting.SetActive(false, Now.UtcDateTime.AddDays(1));
        var reopened = (await _sut.GetAsync(created.Id, CancellationToken.None)).Value;

        var line = Assert.Single(reopened.Lines);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal("CNS-001", line.Sku);
        Assert.Equal("Hour", line.Unit);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);
    }

    [Fact]
    public async Task A_new_Draft_has_no_payments_and_the_payment_figures_are_beside_the_totals_not_inside_them()
    {
        var created = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;

        // Spec 008: the invoice carries paid, remaining and its payments; the totals object stays the invoice's amounts only.
        Assert.Empty(created.Payments);
        Assert.Equal(0.00m, created.Paid);
        Assert.Equal(created.Totals.GrandTotal, created.Remaining);
        Assert.False(created.AcceptsPayment);
        var totalsNames = typeof(InvoiceTotalsDto).GetProperties().Select(property => property.Name).ToList();
        Assert.Contains("GrandTotal", totalsNames);
        Assert.DoesNotContain(totalsNames, name => name is "Paid" or "Remaining" or "Balance");
    }

    // ---- header details ----

    [Fact]
    public async Task A_new_invoice_is_a_Draft_with_equal_timestamps_and_trimmed_notes()
    {
        var input = Input() with { Notes = "  Net 30  " };

        var result = (await _sut.CreateAsync(input, CancellationToken.None)).Value;

        Assert.Equal("Net 30", result.Notes);
        Assert.Equal(Now.UtcDateTime, result.CreatedAtUtc);
        Assert.Equal(result.CreatedAtUtc, result.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, result.Id);
        var stored = Assert.Single(_invoices.All);
        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.Equal(_abc.Id, stored.CustomerId);
    }

    [Fact]
    public async Task Empty_notes_are_saved_as_no_notes()
    {
        var result = (await _sut.CreateAsync(Input() with { Notes = "   " }, CancellationToken.None)).Value;

        Assert.Null(result.Notes);
    }

    [Fact]
    public async Task Numbers_increase_by_one_for_each_invoice_created()
    {
        var first = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;
        var second = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;
        var third = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;

        Assert.Equal(["INV-000001", "INV-000002", "INV-000003"], [first.Number, second.Number, third.Number]);
    }

    [Fact]
    public async Task A_refused_save_uses_no_number_and_stores_nothing()
    {
        await _sut.CreateAsync(Input(), CancellationToken.None);

        var refused = await _sut.CreateAsync(Input() with { DueDate = "2000-01-01" }, CancellationToken.None);
        var next = (await _sut.CreateAsync(Input(), CancellationToken.None)).Value;

        Assert.True(refused.IsFailure);
        Assert.Equal(2, _invoices.AddCalls);
        Assert.Equal("INV-000002", next.Number);
    }

    // ---- lines ----

    [Fact]
    public async Task The_same_product_twice_gives_two_lines_with_positions_1_and_2()
    {
        var result = (await _sut.CreateAsync(Input(null, Line(_consulting, 1m), Line(_consulting, 3.5m)), CancellationToken.None)).Value;

        Assert.Equal([1, 2], result.Lines.Select(line => line.Position));
        Assert.Equal([1m, 3.5m], result.Lines.Select(line => line.Quantity));
        Assert.Equal(2, result.Lines.Select(line => line.Id).Distinct().Count());
    }

    [Fact]
    public async Task Lines_come_back_in_the_order_they_were_added_with_each_products_own_values()
    {
        var web = SeedProduct("WEB-9", "Website build", 5000.50m, 0m);

        var result = (await _sut.CreateAsync(Input(null, Line(web, 1m), Line(_consulting, 2m)), CancellationToken.None)).Value;

        Assert.Equal(["Website build", "Consulting hour"], result.Lines.Select(line => line.ProductName));
        Assert.Equal([5000.50m, 100m], result.Lines.Select(line => line.UnitPrice));
        Assert.Equal([0m, 15m], result.Lines.Select(line => line.VatPercent));
    }

    [Fact]
    public async Task A_product_with_price_0_can_be_added()
    {
        var free = SeedProduct("FREE-1", "Free sample", 0m, 0m);

        var result = await _sut.CreateAsync(Input(null, Line(free, 1m)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Names_in_arabic_are_saved_exactly()
    {
        var arabic = SeedProduct("AR-1", "ساعة استشارة");
        var customer = Customer.Create("شركة الأمل", CustomerType.Company, "a@amal.example", "0500000001", null, "الرياض", Now.UtcDateTime);
        _customers.Seed(customer);

        var result = (await _sut.CreateAsync(new InvoiceInput(customer.Id.ToString(), DueDate, "ملاحظات\nالسطر الثاني", [Line(arabic, 1m)]), CancellationToken.None)).Value;

        Assert.Equal("شركة الأمل", result.Customer.Name);
        Assert.Equal("ساعة استشارة", result.Lines[0].ProductName);
        Assert.Equal("ملاحظات\nالسطر الثاني", result.Notes);
    }

    // ---- customer rules ----

    [Fact]
    public async Task An_inactive_customer_is_refused_naming_customerId_and_nothing_is_stored()
    {
        var dormant = Customer.Create("Dormant", CustomerType.Company, "d@x.example", "0500000002", null, "Riyadh", Now.UtcDateTime);
        dormant.SetActive(false, Now.UtcDateTime);
        _customers.Seed(dormant);

        var errors = ErrorsOf(await _sut.CreateAsync(Input(dormant.Id), CancellationToken.None));

        Assert.Equal("Choose an active customer.", errors["customerId"][0]);
        Assert.Empty(_invoices.All);
    }

    [Fact]
    public async Task An_unknown_customer_is_refused_the_same_way()
    {
        var errors = ErrorsOf(await _sut.CreateAsync(Input(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("Choose an active customer.", errors["customerId"][0]);
        Assert.Empty(_invoices.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task A_missing_or_malformed_customer_is_refused_naming_customerId(string? customerId)
    {
        var input = Input() with { CustomerId = customerId };

        var errors = ErrorsOf(await _sut.CreateAsync(input, CancellationToken.None));

        Assert.True(errors.ContainsKey("customerId"));
        Assert.Empty(_invoices.All);
    }

    // ---- product rules ----

    [Fact]
    public async Task An_inactive_product_is_refused_naming_the_line_and_nothing_is_stored()
    {
        var retired = SeedProduct("OLD-1", "Retired", active: false);

        var errors = ErrorsOf(await _sut.CreateAsync(Input(null, Line(_consulting, 1m), Line(retired, 1m)), CancellationToken.None));

        Assert.False(errors.ContainsKey("lines[0].productId"));
        Assert.Equal("This product is inactive or does not exist. Choose an active product.", errors["lines[1].productId"][0]);
        Assert.Empty(_invoices.All);
    }

    [Fact]
    public async Task An_unknown_product_is_refused_the_same_way()
    {
        var input = Input(null, new InvoiceLineInput(null, Guid.NewGuid().ToString(), 1m));

        var errors = ErrorsOf(await _sut.CreateAsync(input, CancellationToken.None));

        Assert.True(errors.ContainsKey("lines[0].productId"));
        Assert.Empty(_invoices.All);
    }

    [Fact]
    public async Task Every_problem_is_reported_together_shape_customer_and_products()
    {
        var retired = SeedProduct("OLD-1", "Retired", active: false);
        var input = new InvoiceInput(
            Guid.NewGuid().ToString(),
            "2000-01-01",
            null,
            [new InvoiceLineInput(null, retired.Id.ToString(), 0m), new InvoiceLineInput(null, Guid.NewGuid().ToString(), 1m)]);

        var errors = ErrorsOf(await _sut.CreateAsync(input, CancellationToken.None));

        Assert.Equal(
            ["customerId", "dueDate", "lines[0].productId", "lines[0].quantity", "lines[1].productId"],
            errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Empty(_invoices.All);
        Assert.Equal(0, _invoices.AddCalls);
    }

    [Fact]
    public async Task A_bad_shape_never_reaches_the_stores_and_a_line_id_on_create_is_refused()
    {
        var input = Input(null, new InvoiceLineInput(Guid.NewGuid().ToString(), null, 1m));

        var errors = ErrorsOf(await _sut.CreateAsync(input, CancellationToken.None));

        Assert.True(errors.ContainsKey("lines[0].id"));
        Assert.Empty(_invoices.All);
    }

    // ---- today in Saudi Arabia ----

    [Fact]
    public void Today_is_the_calendar_date_at_UTC_plus_3()
    {
        // 22:30 UTC on 14 June is already 01:30 on 15 June in Saudi Arabia.
        var lateEvening = new FixedTimeProvider(new DateTimeOffset(2030, 6, 14, 22, 30, 0, TimeSpan.Zero));
        var earlyMorning = new FixedTimeProvider(new DateTimeOffset(2030, 6, 14, 20, 59, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2030, 6, 15), InvoiceService.Today(lateEvening));
        Assert.Equal(new DateOnly(2030, 6, 14), InvoiceService.Today(earlyMorning));
    }

    [Fact]
    public async Task A_due_date_of_todays_saudi_date_is_accepted_and_the_day_before_is_refused()
    {
        var late = new FixedTimeProvider(new DateTimeOffset(2030, 6, 14, 22, 30, 0, TimeSpan.Zero));
        var sut = new InvoiceService(_invoices, _customers, _products, late, _user);

        var today = await sut.CreateAsync(Input() with { DueDate = "2030-06-15" }, CancellationToken.None);
        var yesterday = await sut.CreateAsync(Input() with { DueDate = "2030-06-14" }, CancellationToken.None);

        Assert.True(today.IsSuccess);
        Assert.True(yesterday.IsFailure);
        Assert.True(yesterday.Error!.Errors!.ContainsKey("dueDate"));
    }

    // ---- get ----

    [Fact]
    public async Task Get_returns_the_invoice_with_its_lines_in_position_order_and_the_customers_current_name_and_state()
    {
        var created = (await _sut.CreateAsync(Input(null, Line(_consulting, 1m), Line(_consulting, 2m)), CancellationToken.None)).Value;
        _abc.SetActive(false, Now.UtcDateTime.AddDays(1));

        var result = await _sut.GetAsync(created.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Number, result.Value.Number);
        Assert.Equal([1, 2], result.Value.Lines.Select(line => line.Position));
        Assert.Equal("ABC", result.Value.Customer.Name);
        Assert.False(result.Value.Customer.IsActive);
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_not_found()
    {
        var result = await _sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal("Invoice not found", result.Error.Title);
        Assert.Equal("No invoice exists with this id.", result.Error.Detail);
    }
}
