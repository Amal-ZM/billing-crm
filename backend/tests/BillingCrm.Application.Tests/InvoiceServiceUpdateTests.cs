using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class InvoiceServiceUpdateTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly FakeCurrentUser _user = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Customer _other;
    private readonly Product _consulting;
    private readonly Product _web;

    public InvoiceServiceUpdateTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);

        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime);
        _other = Customer.Create("Other Co", CustomerType.Company, "accounts@other.example", "0500000001", null, "Jeddah", Start.UtcDateTime);
        _customers.Seed(_abc);
        _customers.Seed(_other);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Start.UtcDateTime);
        _web = Product.Create("Website build", "WEB-9", ProductUnit.Service, 5000.50m, 0m, Start.UtcDateTime);
        _products.Seed(_consulting);
        _products.Seed(_web);
    }

    private static string FutureDue => "2030-08-01";

    private static InvoiceLineInput New(Product product, decimal quantity) => new(null, product.Id.ToString(), quantity);

    private static InvoiceLineInput Keep(InvoiceLineDto line, decimal? quantity = null) => new(line.Id.ToString(), null, quantity ?? line.Quantity);

    private InvoiceInput Edit(Customer? customer = null, string? dueDate = null, string? notes = "Net 30", params InvoiceLineInput[] lines) =>
        new((customer ?? _abc).Id.ToString(), dueDate ?? "2030-07-01", notes, lines);

    /// <summary>An invoice for ABC with one consulting line (quantity 2), due 2030-07-01, made through the service.</summary>
    private async Task<InvoiceDto> CreateAsync(params InvoiceLineInput[] lines) =>
        (await _sut.CreateAsync(Edit(null, null, "Net 30", lines.Length == 0 ? [New(_consulting, 2m)] : lines), CancellationToken.None)).Value;

    private static IReadOnlyDictionary<string, string[]> ErrorsOf(Result<InvoiceDto> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    private Invoice Stored(Guid id) => _invoices.All.Single(invoice => invoice.Id == id);

    // ---- a combined edit ----

    [Fact]
    public async Task One_save_can_change_a_quantity_add_a_line_and_change_the_due_date_notes_and_customer()
    {
        var created = await CreateAsync();
        _time.Advance(TimeSpan.FromHours(2));

        var result = await _sut.UpdateAsync(
            created.Id,
            Edit(_other, "2030-09-15", "  Pay on receipt  ", Keep(created.Lines[0], 3m), New(_web, 1m)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("Other Co", invoice.Customer.Name);
        Assert.Equal("2030-09-15", invoice.DueDate);
        Assert.Equal("Pay on receipt", invoice.Notes);
        Assert.Equal(created.Number, invoice.Number);
        Assert.Equal("Draft", invoice.Status);
        Assert.Equal(created.CreatedAtUtc, invoice.CreatedAtUtc);
        Assert.Equal(Start.AddHours(2).UtcDateTime, invoice.UpdatedAtUtc);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(3m, invoice.Lines[0].Quantity);
        Assert.Equal(created.Lines[0].Id, invoice.Lines[0].Id);
        Assert.Equal("Website build", invoice.Lines[1].ProductName);
        Assert.Equal(5000.50m, invoice.Lines[1].UnitPrice);
        Assert.Equal(0m, invoice.Lines[1].VatPercent);
        Assert.Equal(2, invoice.Lines[1].Position);
        Assert.Equal(1, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task Changing_a_quantity_recalculates_the_amounts_from_the_saved_price_and_VAT()
    {
        var created = await CreateAsync();

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 3m)), CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal(300.00m, invoice.Lines[0].Net);
        Assert.Equal(45.00m, invoice.Lines[0].Vat);
        Assert.Equal(300.00m, invoice.Totals.TotalNet);
        Assert.Equal(45.00m, invoice.Totals.TotalVat);
        Assert.Equal(345.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task Adding_and_removing_lines_are_reflected_in_the_amounts_and_the_amounts_add_up()
    {
        var created = await CreateAsync(New(_consulting, 2m), New(_web, 1m));
        Assert.Equal(200.00m + 5000.50m + 30.00m, created.Totals.GrandTotal);

        // Keep the website line only, and add consulting again at quantity 1.5.
        var result = await _sut.UpdateAsync(
            created.Id,
            Edit(null, null, "Net 30", Keep(created.Lines[1]), New(_consulting, 1.5m)),
            CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal([5000.50m, 150.00m], invoice.Lines.Select(line => line.Net));
        Assert.Equal([0.00m, 22.50m], invoice.Lines.Select(line => line.Vat));
        Assert.Equal(5150.50m, invoice.Totals.TotalNet);
        Assert.Equal(22.50m, invoice.Totals.TotalVat);
        Assert.Equal(5173.00m, invoice.Totals.GrandTotal);
        Assert.Equal(invoice.Lines.Sum(line => line.Net), invoice.Totals.TotalNet);
        Assert.Equal(invoice.Totals.NetAfterDiscount + invoice.Totals.TotalVat, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_kept_line_keeps_its_saved_copy_even_when_the_product_has_changed()
    {
        var created = await CreateAsync();
        _consulting.Update("Consulting day", "CNS-002", ProductUnit.Service, 999m, 0m, Start.UtcDateTime.AddDays(1));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 5m)), CancellationToken.None);

        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(5m, line.Quantity);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal("CNS-001", line.Sku);
        Assert.Equal("Hour", line.Unit);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);
    }

    [Fact]
    public async Task A_new_line_copies_the_products_current_values_not_an_older_ones()
    {
        var created = await CreateAsync();
        _web.Update("Website build v2", "WEB-9", ProductUnit.Service, 6000m, 5m, Start.UtcDateTime.AddDays(1));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), New(_web, 1m)), CancellationToken.None);

        var added = result.Value.Lines[1];
        Assert.Equal("Website build v2", added.ProductName);
        Assert.Equal(6000m, added.UnitPrice);
        Assert.Equal(5m, added.VatPercent);
    }

    [Fact]
    public async Task Existing_lines_stay_in_their_original_order_whatever_order_they_arrive_in_and_new_lines_go_last_in_the_order_sent()
    {
        var created = await CreateAsync(New(_consulting, 1m), New(_web, 2m));

        var result = await _sut.UpdateAsync(
            created.Id,
            Edit(null, null, "Net 30", New(_consulting, 9m), Keep(created.Lines[1]), New(_web, 8m), Keep(created.Lines[0])),
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], result.Value.Lines.Select(line => line.Position));
        Assert.Equal([1m, 2m, 9m, 8m], result.Value.Lines.Select(line => line.Quantity));
    }

    [Fact]
    public async Task A_line_can_be_removed_while_another_remains_and_positions_are_not_renumbered()
    {
        var created = await CreateAsync(New(_consulting, 1m), New(_web, 2m));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[1])), CancellationToken.None);

        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(created.Lines[1].Id, line.Id);
        Assert.Equal(2, line.Position);
        Assert.Equal("Website build", line.ProductName);
    }

    [Fact]
    public async Task A_line_added_after_the_last_one_was_removed_takes_the_next_position_after_the_highest_that_remains()
    {
        var created = await CreateAsync(New(_consulting, 1m), New(_web, 2m));
        var afterRemoval = (await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])), CancellationToken.None)).Value;

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(afterRemoval.Lines[0]), New(_web, 4m)), CancellationToken.None);

        Assert.Equal([1, 2], result.Value.Lines.Select(line => line.Position));
    }

    [Fact]
    public async Task The_same_product_can_be_added_again_as_another_line()
    {
        var created = await CreateAsync();

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), New(_consulting, 1.5m)), CancellationToken.None);

        Assert.Equal(2, result.Value.Lines.Count);
        Assert.Equal([2m, 1.5m], result.Value.Lines.Select(line => line.Quantity));
    }

    // ---- the last line and the limits ----

    [Fact]
    public async Task Removing_the_last_line_is_refused_and_the_invoice_is_unchanged()
    {
        var created = await CreateAsync();

        var errors = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30"), CancellationToken.None));

        Assert.True(errors.ContainsKey("lines"));
        Assert.Single(Stored(created.Id).Lines);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task More_than_100_lines_are_refused()
    {
        var created = await CreateAsync();
        var lines = new[] { Keep(created.Lines[0]) }.Concat(Enumerable.Range(0, 100).Select(_ => New(_web, 1m))).ToArray();

        var errors = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", lines), CancellationToken.None));

        Assert.True(errors.ContainsKey("lines"));
        Assert.Single(Stored(created.Id).Lines);
    }

    [Fact]
    public async Task Exactly_100_lines_are_accepted()
    {
        var created = await CreateAsync();
        var lines = new[] { Keep(created.Lines[0]) }.Concat(Enumerable.Range(0, 99).Select(_ => New(_web, 1m))).ToArray();

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", lines), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value.Lines.Count);
    }

    // ---- line identity ----

    [Fact]
    public async Task A_line_id_that_belongs_to_another_invoice_is_refused_naming_the_line()
    {
        var mine = await CreateAsync();
        var theirs = await CreateAsync(New(_web, 1m));

        var errors = ErrorsOf(await _sut.UpdateAsync(mine.Id, Edit(null, null, "Net 30", Keep(theirs.Lines[0])), CancellationToken.None));

        Assert.Equal("This line does not belong to this invoice.", errors["lines[0].id"][0]);
        Assert.Equal(2m, Stored(mine.Id).Lines[0].Quantity);
    }

    [Fact]
    public async Task An_unknown_line_id_a_duplicated_id_and_a_line_with_both_or_neither_are_refused_naming_the_line()
    {
        var created = await CreateAsync();
        var known = created.Lines[0];

        var unknown = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", new InvoiceLineInput(Guid.NewGuid().ToString(), null, 1m)), CancellationToken.None));
        var duplicate = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(known), Keep(known)), CancellationToken.None));
        var both = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", new InvoiceLineInput(known.Id.ToString(), _web.Id.ToString(), 1m)), CancellationToken.None));
        var neither = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", new InvoiceLineInput(null, null, 1m)), CancellationToken.None));

        Assert.True(unknown.ContainsKey("lines[0].id"));
        Assert.True(duplicate.ContainsKey("lines[1].id"));
        Assert.True(both.ContainsKey("lines[0].id"));
        Assert.True(neither.ContainsKey("lines[0].productId"));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    // ---- the customer ----

    [Fact]
    public async Task Keeping_the_same_customer_is_allowed_even_after_it_became_inactive()
    {
        var created = await CreateAsync();
        _abc.SetActive(false, Start.UtcDateTime.AddDays(1));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, "2030-07-10", "Changed notes", Keep(created.Lines[0], 4m)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ABC", result.Value.Customer.Name);
        Assert.False(result.Value.Customer.IsActive);
        Assert.Equal(4m, result.Value.Lines[0].Quantity);
        Assert.Equal("Changed notes", result.Value.Notes);
    }

    [Fact]
    public async Task A_different_customer_must_be_active_unknown_or_inactive_is_refused()
    {
        var created = await CreateAsync();
        var dormant = Customer.Create("Dormant", CustomerType.Company, "d@x.example", "0500000002", null, "Riyadh", Start.UtcDateTime);
        dormant.SetActive(false, Start.UtcDateTime);
        _customers.Seed(dormant);

        var inactive = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(dormant, null, "Net 30", Keep(created.Lines[0])), CancellationToken.None));
        var unknown = ErrorsOf(await _sut.UpdateAsync(
            created.Id,
            Edit(null, null, "Net 30", Keep(created.Lines[0])) with { CustomerId = Guid.NewGuid().ToString() },
            CancellationToken.None));

        Assert.Equal("Choose an active customer.", inactive["customerId"][0]);
        Assert.True(unknown.ContainsKey("customerId"));
        Assert.Equal(_abc.Id, Stored(created.Id).CustomerId);
    }

    [Fact]
    public async Task Changing_to_another_active_customer_works()
    {
        var created = await CreateAsync();

        var result = await _sut.UpdateAsync(created.Id, Edit(_other, null, "Net 30", Keep(created.Lines[0])), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Other Co", result.Value.Customer.Name);
        Assert.Equal(_other.Id, Stored(created.Id).CustomerId);
    }

    // ---- products that became inactive ----

    [Fact]
    public async Task An_existing_line_for_a_now_inactive_product_can_be_kept_changed_or_removed()
    {
        var created = await CreateAsync(New(_consulting, 2m), New(_web, 1m));
        _consulting.SetActive(false, Start.UtcDateTime.AddDays(1));

        var kept = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), Keep(created.Lines[1])), CancellationToken.None);
        var changed = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 7m), Keep(created.Lines[1])), CancellationToken.None);
        var removed = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[1])), CancellationToken.None);

        Assert.True(kept.IsSuccess);
        Assert.True(changed.IsSuccess);
        Assert.Equal(7m, changed.Value.Lines[0].Quantity);
        Assert.True(removed.IsSuccess);
        Assert.Single(removed.Value.Lines);
    }

    [Fact]
    public async Task A_new_line_for_an_inactive_product_is_refused_naming_the_line_and_nothing_changes()
    {
        var created = await CreateAsync();
        _web.SetActive(false, Start.UtcDateTime.AddDays(1));

        var errors = ErrorsOf(await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 9m), New(_web, 1m)), CancellationToken.None));

        Assert.Equal("This product is inactive or does not exist. Choose an active product.", errors["lines[1].productId"][0]);
        Assert.Equal(2m, Stored(created.Id).Lines[0].Quantity);
        Assert.Single(Stored(created.Id).Lines);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    // ---- the due date ----

    [Fact]
    public async Task An_unchanged_past_due_date_is_accepted_and_a_changed_past_date_is_not()
    {
        var past = Invoice.Create(_abc.Id, new DateOnly(2030, 5, 1), null, Start.UtcDateTime);
        past.AddLine(_consulting, 1m, Start.UtcDateTime);
        _invoices.Seed(past);
        var lineId = past.Lines[0].Id.ToString();

        var unchanged = await _sut.UpdateAsync(past.Id, Edit(null, "2030-05-01", "Now with notes", new InvoiceLineInput(lineId, null, 3m)), CancellationToken.None);
        var otherPast = await _sut.UpdateAsync(past.Id, Edit(null, "2030-05-02", "Now with notes", new InvoiceLineInput(lineId, null, 3m)), CancellationToken.None);
        var future = await _sut.UpdateAsync(past.Id, Edit(null, "2030-07-01", "Now with notes", new InvoiceLineInput(lineId, null, 3m)), CancellationToken.None);

        Assert.True(unchanged.IsSuccess);
        Assert.True(otherPast.IsFailure);
        Assert.True(otherPast.Error!.Errors!.ContainsKey("dueDate"));
        Assert.True(future.IsSuccess);
    }

    // ---- nothing changes when nothing changed, or when anything is refused ----

    // ---- the discount and the limits (spec 006, story 2) ----

    private async Task<InvoiceDto> CreateWithDiscountAsync(decimal discount, params InvoiceLineInput[] lines) =>
        (await _sut.CreateAsync(Edit(null, null, "Net 30", lines.Length == 0 ? [New(_consulting, 2m)] : lines) with { Discount = discount }, CancellationToken.None)).Value;

    [Fact]
    public async Task A_discount_can_be_added_to_a_saved_Draft()
    {
        var created = await CreateAsync();

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])) with { Discount = 20.00m }, CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal(20.00m, invoice.Discount);
        Assert.Equal(180.00m, invoice.Totals.NetAfterDiscount);
        Assert.Equal(27.00m, invoice.Totals.TotalVat);
        Assert.Equal(207.00m, invoice.Totals.GrandTotal);
        Assert.Equal(20.00m, Stored(created.Id).Discount);
        Assert.Equal(1, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task A_discount_equal_to_the_whole_net_is_accepted_on_an_edit()
    {
        var created = await CreateAsync();

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])) with { Discount = 200.00m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.00m, result.Value.Totals.NetAfterDiscount);
        Assert.Equal(0.00m, result.Value.Totals.GrandTotal);
    }

    [Fact]
    public async Task Twenty_ten_over_a_twenty_net_is_refused_on_the_discount()
    {
        var cheap = Product.Create("Cheap", "CH-1", ProductUnit.Item, 10.00m, 15m, Start.UtcDateTime);
        _products.Seed(cheap);
        var created = await CreateAsync(New(cheap, 2m));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])) with { Discount = 20.10m }, CancellationToken.None);

        Assert.Equal(["Discount cannot be more than the total net of the lines."], ErrorsOf(result)["discount"]);
    }

    [Fact]
    public async Task Leaving_the_discount_out_of_an_edit_clears_a_saved_discount()
    {
        var created = await CreateWithDiscountAsync(20.00m);

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])), CancellationToken.None);

        Assert.Equal(0.00m, result.Value.Discount);
        Assert.Equal(230.00m, result.Value.Totals.GrandTotal);
        Assert.Equal(0m, Stored(created.Id).Discount);
    }

    [Fact]
    public async Task Sending_the_saved_discount_back_with_a_new_quantity_keeps_it_and_recalculates_the_amounts()
    {
        var created = await CreateWithDiscountAsync(20.00m);

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 3m)) with { Discount = 20.00m }, CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal(300.00m, invoice.Totals.TotalNet);
        Assert.Equal(280.00m, invoice.Totals.NetAfterDiscount);
        Assert.Equal(42.00m, invoice.Totals.TotalVat);
        Assert.Equal(322.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_discount_above_the_total_net_is_refused_and_the_invoice_is_unchanged()
    {
        var created = await CreateAsync();
        var before = Snapshot(Stored(created.Id));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])) with { Discount = 250m }, CancellationToken.None);

        Assert.Equal(["Discount cannot be more than the total net of the lines."], ErrorsOf(result)["discount"]);
        Assert.Equal(before, Snapshot(Stored(created.Id)));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task An_edit_that_lowers_the_net_below_the_saved_discount_is_refused_and_nothing_changes()
    {
        var created = await CreateWithDiscountAsync(150.00m, New(_consulting, 2m), New(_web, 1m));
        var before = Snapshot(Stored(created.Id));

        // Saved: net 5,200.50 with a 150.00 discount. The edit drops the website line and lowers the kept line to quantity 1 (net 100.00),
        // so the same 150.00 discount is now more than the net.
        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 1m)) with { Discount = 150.00m }, CancellationToken.None);

        Assert.Equal(["Discount cannot be more than the total net of the lines."], ErrorsOf(result)["discount"]);
        Assert.Equal(before, Snapshot(Stored(created.Id)));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task A_new_line_that_is_too_large_is_refused_and_the_invoice_is_unchanged()
    {
        var created = await CreateAsync();
        var before = Snapshot(Stored(created.Id));
        var huge = Product.Create("Huge price", "HUGE-1", ProductUnit.Item, 999_999_999.99m, 15m, Start.UtcDateTime);
        _products.Seed(huge);

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), New(huge, 999_999.99m)), CancellationToken.None);

        Assert.Equal(["The amount of this line is too large. Reduce the quantity."], ErrorsOf(result)["lines[1].quantity"]);
        Assert.Equal(before, Snapshot(Stored(created.Id)));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task An_edit_whose_total_is_too_large_is_refused_on_the_lines_and_the_invoice_is_unchanged()
    {
        var created = await CreateAsync();
        var before = Snapshot(Stored(created.Id));
        var big = Product.Create("Big price", "BIG-1", ProductUnit.Item, 999_999_999.99m, 0m, Start.UtcDateTime);
        _products.Seed(big);

        // Two new lines of 8,999,999,999,910.00 each: each is allowed, together they are above the ceiling.
        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), New(big, 9_000m), New(big, 9_000m)), CancellationToken.None);

        Assert.Equal(["The invoice total is too large. Reduce the quantities or remove lines."], ErrorsOf(result)["lines"]);
        Assert.Equal(before, Snapshot(Stored(created.Id)));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task The_size_check_uses_the_order_the_saved_invoice_will_calculate_in_and_names_the_requests_own_line()
    {
        // Two saved lines; the edit lists them in the opposite order and adds an oversize new line first.
        var created = await CreateAsync(New(_consulting, 2m), New(_web, 1m));
        var huge = Product.Create("Huge price", "HUGE-1", ProductUnit.Item, 999_999_999.99m, 15m, Start.UtcDateTime);
        _products.Seed(huge);

        var result = await _sut.UpdateAsync(
            created.Id,
            Edit(null, null, "Net 30", New(huge, 999_999.99m), Keep(created.Lines[1]), Keep(created.Lines[0])),
            CancellationToken.None);

        Assert.Equal(["lines[0].quantity"], ErrorsOf(result).Keys);
    }

    [Fact]
    public async Task Existing_lines_are_checked_at_their_saved_price_so_a_repriced_product_cannot_make_them_too_large()
    {
        var created = await CreateAsync();
        _consulting.Update("Consulting hour", "CNS-001", ProductUnit.Hour, 999_999_999.99m, 15m, Start.UtcDateTime.AddDays(1));

        // The kept line is still 100.00 each, so quantity 999,999.99 is 99,999,999.00: fine. Priced at the product's new price it would be too large.
        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 999_999.99m)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99_999_999.00m, result.Value.Totals.TotalNet);
    }

    [Fact]
    public async Task Saving_the_same_lines_and_the_same_discount_writes_nothing()
    {
        var created = await CreateWithDiscountAsync(20.00m);

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0])) with { Discount = 20.00m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task When_other_errors_exist_the_discount_above_net_check_is_skipped()
    {
        var created = await CreateAsync();
        _web.SetActive(false, Start.UtcDateTime.AddDays(1));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), New(_web, 1m)) with { Discount = 999_999m }, CancellationToken.None);

        Assert.Equal(["lines[1].productId"], ErrorsOf(result).Keys);
    }

    // ---- amounts stay true when products change (spec 006, story 3) ----

    [Fact]
    public async Task Amounts_do_not_change_when_the_product_is_repriced_renamed_and_deactivated()
    {
        var created = await CreateAsync();
        _consulting.Update("Consulting day", "CNS-002", ProductUnit.Service, 150.00m, 5m, Start.UtcDateTime.AddDays(1));
        _consulting.SetActive(false, Start.UtcDateTime.AddDays(1));

        var opened = (await _sut.GetAsync(created.Id, CancellationToken.None)).Value;

        Assert.Equal(230.00m, opened.Totals.GrandTotal);
        Assert.Equal(created.Totals, opened.Totals);
        Assert.Equal(created.Lines.Select(line => (line.Net, line.DiscountShare, line.Vat)), opened.Lines.Select(line => (line.Net, line.DiscountShare, line.Vat)));
    }

    [Fact]
    public async Task The_amounts_with_a_saved_discount_do_not_change_when_the_product_changes()
    {
        var created = await CreateWithDiscountAsync(20.00m);
        _consulting.Update("Consulting hour", "CNS-001", ProductUnit.Hour, 999.00m, 0m, Start.UtcDateTime.AddDays(1));

        var opened = (await _sut.GetAsync(created.Id, CancellationToken.None)).Value;

        Assert.Equal(207.00m, opened.Totals.GrandTotal);
        Assert.Equal(created.Totals, opened.Totals);
    }

    [Fact]
    public async Task Editing_a_quantity_after_the_product_was_repriced_uses_the_saved_price_and_VAT()
    {
        var created = await CreateAsync();
        _consulting.Update("Consulting hour", "CNS-001", ProductUnit.Hour, 150.00m, 5m, Start.UtcDateTime.AddDays(1));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0], 3m)), CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal(300.00m, invoice.Totals.TotalNet);
        Assert.Equal(45.00m, invoice.Totals.TotalVat);
        Assert.Equal(345.00m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_new_line_for_the_repriced_product_uses_the_current_price_and_VAT_while_the_kept_line_keeps_its_amounts()
    {
        var created = await CreateAsync();
        _consulting.Update("Consulting hour", "CNS-001", ProductUnit.Hour, 150.00m, 5m, Start.UtcDateTime.AddDays(1));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Net 30", Keep(created.Lines[0]), New(_consulting, 1m)), CancellationToken.None);

        var invoice = result.Value;
        Assert.Equal([200.00m, 150.00m], invoice.Lines.Select(line => line.Net));
        Assert.Equal([30.00m, 7.50m], invoice.Lines.Select(line => line.Vat));
        Assert.Equal(350.00m, invoice.Totals.TotalNet);
        Assert.Equal(37.50m, invoice.Totals.TotalVat);
        Assert.Equal(387.50m, invoice.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_writes_nothing_and_leaves_the_changed_time_alone()
    {
        var created = await CreateAsync();
        _time.Advance(TimeSpan.FromHours(5));

        var result = await _sut.UpdateAsync(created.Id, Edit(null, "2030-07-01", "  Net 30 ", Keep(created.Lines[0])), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.UpdatedAtUtc, result.Value.UpdatedAtUtc);
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task Any_refusal_leaves_the_invoice_exactly_as_it_was_and_reports_every_problem_together()
    {
        var created = await CreateAsync(New(_consulting, 2m), New(_web, 1m));
        var before = Snapshot(Stored(created.Id));
        var dormant = Customer.Create("Dormant", CustomerType.Company, "d@x.example", "0500000002", null, "Riyadh", Start.UtcDateTime);
        dormant.SetActive(false, Start.UtcDateTime);
        _customers.Seed(dormant);
        var retired = Product.Create("Retired", "OLD-1", ProductUnit.Item, 1m, 15m, Start.UtcDateTime);
        retired.SetActive(false, Start.UtcDateTime);
        _products.Seed(retired);

        var errors = ErrorsOf(await _sut.UpdateAsync(
            created.Id,
            new InvoiceInput(dormant.Id.ToString(), "2000-01-01", "changed", [Keep(created.Lines[0], 50m), New(retired, 0m)]),
            CancellationToken.None));

        Assert.Equal(
            ["customerId", "dueDate", "lines[1].productId", "lines[1].quantity"],
            errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(before, Snapshot(Stored(created.Id)));
        Assert.Equal(0, _invoices.SaveChangesCalls);
    }

    [Fact]
    public async Task A_save_that_lost_a_race_with_another_edit_is_a_conflict_not_an_exception()
    {
        var created = await CreateAsync();
        _invoices.SaveLosesRace = true;

        var result = await _sut.UpdateAsync(created.Id, Edit(null, null, "Changed", Keep(created.Lines[0], 5m)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal("invoice_changed", result.Error.Code);
        Assert.Equal("This invoice was changed by someone else. Reload it and try again.", result.Error.Detail);
    }

    [Fact]
    public async Task An_unknown_invoice_is_not_found()
    {
        var result = await _sut.UpdateAsync(Guid.NewGuid(), Edit(null, null, "Net 30", New(_consulting, 1m)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal("Invoice not found", result.Error.Title);
    }

    [Fact]
    public async Task An_invoice_that_is_not_found_is_reported_before_the_body_is_judged()
    {
        var result = await _sut.UpdateAsync(Guid.NewGuid(), new InvoiceInput(null, null, null, null), CancellationToken.None);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    private static string Snapshot(Invoice invoice) =>
        $"{invoice.CustomerId}|{invoice.DueDate:O}|{invoice.Notes}|{invoice.Discount}|{invoice.UpdatedAtUtc:O}|" +
        string.Join(";", invoice.Lines.Select(line => $"{line.Id}:{line.Position}:{line.ProductName}:{line.UnitPrice}:{line.VatPercent}:{line.Quantity}"));
}
