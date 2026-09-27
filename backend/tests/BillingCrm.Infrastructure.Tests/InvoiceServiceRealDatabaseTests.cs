using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Products;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Persistence;
using BillingCrm.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// The whole create-and-read path with the real stores on real SQL Server: the same <see cref="InvoiceService"/> the API uses, no
/// fakes. It stands in for a manual check of the running API: the acceptance scenario, the saved copy surviving product changes,
/// the refusals, and the number sequence.
/// </summary>
public class InvoiceServiceRealDatabaseTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Due() => DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    /// <summary>A service over its own context, like one API request.</summary>
    private InvoiceService NewService(out BillingDbContext context)
    {
        context = database.CreateContext();
        return new InvoiceService(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser());
    }

    private async Task<Customer> AddCustomerAsync(string name = "ABC", bool active = true)
    {
        var customer = Customer.Create(name, CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        if (!active)
        {
            customer.SetActive(false, Now);
        }

        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync(string name = "Consulting hour", decimal price = 100m, decimal vat = 15m, bool active = true)
    {
        var product = Product.Create(name, Unique("P"), ProductUnit.Hour, price, vat, Now);
        if (!active)
        {
            product.SetActive(false, Now);
        }

        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private static InvoiceInput Input(Customer customer, params InvoiceLineInput[] lines) =>
        new(customer.Id.ToString(), Due(), "Net 30", lines);

    private static InvoiceLineInput Line(Product product, decimal quantity) => new(null, product.Id.ToString(), quantity);

    [Fact]
    public async Task Customer_ABC_and_a_product_with_quantity_2_is_saved_as_a_Draft_with_one_line_and_can_be_read_back()
    {
        var customer = await AddCustomerAsync("ABC");
        var product = await AddProductAsync("Consulting hour", 100m, 15m);

        var service = NewService(out var writeContext);
        await using var _w = writeContext;
        var created = await service.CreateAsync(Input(customer, Line(product, 2m)), default);

        Assert.True(created.IsSuccess);
        Assert.Equal("Draft", created.Value.Status);
        Assert.Matches("^INV-[0-9]{6}$", created.Value.Number);
        Assert.Equal("ABC", created.Value.Customer.Name);
        var line = Assert.Single(created.Value.Lines);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);

        // A brand new context, like the next request: everything was really saved.
        var reader = NewService(out var readContext);
        await using var _r = readContext;
        var reopened = (await reader.GetAsync(created.Value.Id, default)).Value;
        Assert.Equal(created.Value.Number, reopened.Number);
        Assert.Equal("Draft", reopened.Status);
        Assert.Equal("Net 30", reopened.Notes);
        Assert.Single(reopened.Lines);
        Assert.Equal(product.Sku, reopened.Lines[0].Sku);
        Assert.Equal("Consulting hour", reopened.Lines[0].ProductName);
    }

    [Fact]
    public async Task The_saved_line_survives_the_product_being_renamed_repriced_and_deactivated_through_the_real_product_service()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var creator = NewService(out var creatorContext);
        await using var _c = creatorContext;
        var created = (await creator.CreateAsync(Input(customer, Line(product, 2m)), default)).Value;

        // The real ProductService edits and deactivates the real product, exactly as an Admin would.
        await using (var productContext = database.CreateContext())
        {
            var products = new ProductService(new EfProductStore(productContext), TimeProvider.System);
            var edit = await products.UpdateAsync(product.Id, new ProductInput("Consulting day", Unique("N"), "Service", 999.99m, 0m), default);
            Assert.True(edit.IsSuccess);
            Assert.True((await products.SetActiveAsync(product.Id, new ProductActiveRequest(false), default)).IsSuccess);
        }

        var reader = NewService(out var readContext);
        await using var _r = readContext;
        var reopened = (await reader.GetAsync(created.Id, default)).Value;
        var line = Assert.Single(reopened.Lines);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal(product.Sku, line.Sku);
        Assert.Equal("Hour", line.Unit);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);

        // And the product is now inactive, so a new invoice cannot use it.
        var another = NewService(out var anotherContext);
        await using var _a = anotherContext;
        var refused = await another.CreateAsync(Input(customer, Line(product, 1m)), default);
        Assert.True(refused.IsFailure);
        Assert.True(refused.Error!.Errors!.ContainsKey("lines[0].productId"));
    }

    [Fact]
    public async Task An_inactive_customer_an_inactive_product_and_an_invalid_quantity_are_refused_together_and_nothing_is_saved()
    {
        var dormant = await AddCustomerAsync("Dormant", active: false);
        var retired = await AddProductAsync("Retired", active: false);
        var before = await CountInvoicesAsync();

        var service = NewService(out var context);
        await using var _ = context;
        var result = await service.CreateAsync(
            new InvoiceInput(dormant.Id.ToString(), "2000-01-01", null, [Line(retired, 0m)]),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(
            ["customerId", "dueDate", "lines[0].productId", "lines[0].quantity"],
            result.Error.Errors!.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(before, await CountInvoicesAsync());
    }

    [Fact]
    public async Task Consecutive_invoices_get_increasing_numbers_and_a_refused_one_does_not_break_the_sequence()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();

        async Task<string> CreateNumberAsync()
        {
            var service = NewService(out var context);
            await using var _ = context;
            return (await service.CreateAsync(Input(customer, Line(product, 1m)), default)).Value.Number;
        }

        var first = await CreateNumberAsync();
        var refusedService = NewService(out var refusedContext);
        await using (refusedContext)
        {
            Assert.True((await refusedService.CreateAsync(Input(customer, Line(product, 0m)), default)).IsFailure);
        }

        var second = await CreateNumberAsync();

        Assert.True(InvoiceNumber.TryParse(first, out var a));
        Assert.True(InvoiceNumber.TryParse(second, out var b));
        Assert.True(b > a);
    }

    [Fact]
    public async Task An_unknown_invoice_is_not_found()
    {
        var service = NewService(out var context);
        await using var _ = context;

        var result = await service.GetAsync(Guid.NewGuid(), default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    // ---- editing a Draft (user story 2) ----

    private static InvoiceLineInput Keep(InvoiceLineDto line, decimal? quantity = null) => new(line.Id.ToString(), null, quantity ?? line.Quantity);

    private async Task<InvoiceDto> CreateDraftAsync(Customer customer, params InvoiceLineInput[] lines)
    {
        var service = NewService(out var context);
        await using var _ = context;
        return (await service.CreateAsync(Input(customer, lines), default)).Value;
    }

    private async Task<Result<InvoiceDto>> EditAsync(Guid id, InvoiceInput input)
    {
        var service = NewService(out var context);
        await using var _ = context;
        return await service.UpdateAsync(id, input, default);
    }

    private async Task<InvoiceDto> ReadAsync(Guid id)
    {
        var service = NewService(out var context);
        await using var _ = context;
        return (await service.GetAsync(id, default)).Value;
    }

    [Fact]
    public async Task One_save_changes_a_quantity_adds_a_line_removes_a_line_and_changes_the_header_and_all_of_it_is_saved()
    {
        var abc = await AddCustomerAsync("ABC");
        var other = await AddCustomerAsync("Other Co");
        var consulting = await AddProductAsync("Consulting hour", 100m, 15m);
        var web = await AddProductAsync("Website build", 5000.50m, 0m);
        var extra = await AddProductAsync("Extra", 10m, 15m);
        var created = await CreateDraftAsync(abc, Line(consulting, 2m), Line(web, 1m));

        var edited = await EditAsync(
            created.Id,
            new InvoiceInput(other.Id.ToString(), DateTime.UtcNow.AddDays(60).ToString("yyyy-MM-dd"), "Pay on receipt",
                [Keep(created.Lines[0], 3m), Line(extra, 1.5m)]));

        Assert.True(edited.IsSuccess);
        var reopened = await ReadAsync(created.Id);
        Assert.Equal(created.Number, reopened.Number);
        Assert.Equal("Other Co", reopened.Customer.Name);
        Assert.Equal("Pay on receipt", reopened.Notes);
        Assert.Equal(2, reopened.Lines.Count);
        Assert.Equal(created.Lines[0].Id, reopened.Lines[0].Id);
        Assert.Equal(3m, reopened.Lines[0].Quantity);
        Assert.Equal(100m, reopened.Lines[0].UnitPrice);
        Assert.Equal("Extra", reopened.Lines[1].ProductName);
        Assert.Equal(1.5m, reopened.Lines[1].Quantity);
        Assert.DoesNotContain(reopened.Lines, line => line.ProductName == "Website build");
        Assert.True(reopened.UpdatedAtUtc > created.UpdatedAtUtc);
        Assert.Equal(created.CreatedAtUtc, reopened.CreatedAtUtc);
    }

    [Fact]
    public async Task A_kept_line_keeps_its_saved_price_after_the_product_is_repriced_and_a_new_line_gets_the_new_price()
    {
        var abc = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var created = await CreateDraftAsync(abc, Line(product, 2m));

        await using (var productContext = database.CreateContext())
        {
            var products = new ProductService(new EfProductStore(productContext), TimeProvider.System);
            Assert.True((await products.UpdateAsync(product.Id, new ProductInput("Consulting hour", product.Sku, "Hour", 150m, 0m), default)).IsSuccess);
        }

        var edited = await EditAsync(created.Id, Input(abc, Keep(created.Lines[0], 4m), Line(product, 1m)));

        Assert.True(edited.IsSuccess);
        var lines = (await ReadAsync(created.Id)).Lines;
        Assert.Equal([100m, 150m], lines.Select(line => line.UnitPrice));
        Assert.Equal([15m, 0m], lines.Select(line => line.VatPercent));
        Assert.Equal([4m, 1m], lines.Select(line => line.Quantity));
    }

    [Fact]
    public async Task A_refused_edit_changes_nothing_in_the_database()
    {
        var abc = await AddCustomerAsync();
        var dormant = await AddCustomerAsync("Dormant", active: false);
        var consulting = await AddProductAsync("Consulting hour");
        var retired = await AddProductAsync("Retired", active: false);
        var created = await CreateDraftAsync(abc, Line(consulting, 2m));
        var before = await ReadAsync(created.Id);

        var refused = await EditAsync(
            created.Id,
            new InvoiceInput(dormant.Id.ToString(), "2000-01-01", "changed", [Keep(created.Lines[0], 9m), Line(retired, 0m)]));

        Assert.True(refused.IsFailure);
        Assert.Equal(
            ["customerId", "dueDate", "lines[1].productId", "lines[1].quantity"],
            refused.Error!.Errors!.Keys.OrderBy(key => key, StringComparer.Ordinal));
        var after = await ReadAsync(created.Id);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.DueDate, after.DueDate);
        Assert.Equal(before.Customer.Id, after.Customer.Id);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(before.Lines.Select(line => (line.Id, line.Quantity)), after.Lines.Select(line => (line.Id, line.Quantity)));
    }

    [Fact]
    public async Task A_Draft_can_lose_a_line_but_never_its_last_one()
    {
        var abc = await AddCustomerAsync();
        var one = await AddProductAsync("One");
        var two = await AddProductAsync("Two");
        var created = await CreateDraftAsync(abc, Line(one, 1m), Line(two, 2m));

        var oneLeft = await EditAsync(created.Id, Input(abc, Keep(created.Lines[1])));
        var none = await EditAsync(created.Id, new InvoiceInput(abc.Id.ToString(), Due(), null, []));

        Assert.True(oneLeft.IsSuccess);
        Assert.Single((await ReadAsync(created.Id)).Lines);
        Assert.True(none.IsFailure);
        Assert.True(none.Error!.Errors!.ContainsKey("lines"));
        Assert.Single((await ReadAsync(created.Id)).Lines);
    }

    [Fact]
    public async Task A_Draft_whose_product_and_customer_became_inactive_stays_editable_within_the_rules()
    {
        var abc = await AddCustomerAsync("ABC");
        var consulting = await AddProductAsync("Consulting hour");
        var web = await AddProductAsync("Website build");
        var created = await CreateDraftAsync(abc, Line(consulting, 2m));

        await using (var context = database.CreateContext())
        {
            var products = new ProductService(new EfProductStore(context), TimeProvider.System);
            Assert.True((await products.SetActiveAsync(consulting.Id, new ProductActiveRequest(false), default)).IsSuccess);
            var customerStore = new EfCustomerStore(context);
            var customerService = new CustomerService(customerStore, TimeProvider.System);
            Assert.True((await customerService.SetActiveAsync(abc.Id, new SetActiveRequest(false), default)).IsSuccess);
        }

        var kept = await EditAsync(created.Id, Input(abc, Keep(created.Lines[0], 6m)));
        var newForInactive = await EditAsync(created.Id, Input(abc, Keep(created.Lines[0]), Line(consulting, 1m)));
        var newForActive = await EditAsync(created.Id, Input(abc, Keep(created.Lines[0]), Line(web, 1m)));

        Assert.True(kept.IsSuccess);
        Assert.False(kept.Value.Customer.IsActive);
        Assert.Equal(6m, kept.Value.Lines[0].Quantity);
        Assert.True(newForInactive.IsFailure);
        Assert.True(newForInactive.Error!.Errors!.ContainsKey("lines[1].productId"));
        Assert.True(newForActive.IsSuccess);
        Assert.Equal(2, newForActive.Value.Lines.Count);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_leaves_the_changed_time_alone_and_an_unknown_invoice_is_not_found()
    {
        var abc = await AddCustomerAsync();
        var consulting = await AddProductAsync("Consulting hour");
        var created = await CreateDraftAsync(abc, Line(consulting, 2m));

        var same = await EditAsync(
            created.Id,
            new InvoiceInput(abc.Id.ToString(), created.DueDate, created.Notes, [Keep(created.Lines[0])]));
        var unknown = await EditAsync(Guid.NewGuid(), Input(abc, Line(consulting, 1m)));

        Assert.True(same.IsSuccess);
        Assert.Equal(created.UpdatedAtUtc, (await ReadAsync(created.Id)).UpdatedAtUtc);
        Assert.Equal(ErrorKind.NotFound, unknown.Error!.Kind);
    }

    private async Task<int> CountInvoicesAsync()
    {
        await using var context = database.CreateContext();
        return await context.Invoices.CountAsync();
    }

    // ---- the discount (spec 006) on real SQL ----

    [Fact]
    public async Task A_discount_is_saved_and_the_amounts_are_the_same_when_the_invoice_is_read_back_in_a_new_context()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var service = NewService(out var writeContext);
        await using var _w = writeContext;

        var created = await service.CreateAsync(Input(customer, Line(product, 2m)) with { Discount = 20.10m }, default);

        Assert.True(created.IsSuccess);
        Assert.Equal(20.10m, created.Value.Totals.Discount);
        Assert.Equal(179.90m, created.Value.Totals.NetAfterDiscount);
        Assert.Equal(26.99m, created.Value.Totals.TotalVat);
        Assert.Equal(206.89m, created.Value.Totals.GrandTotal);

        var reader = NewService(out var readContext);
        await using var _r = readContext;
        var reopened = (await reader.GetAsync(created.Value.Id, default)).Value;
        Assert.Equal(20.10m, reopened.Discount);
        Assert.Equal(created.Value.Totals, reopened.Totals);
        Assert.Equal(20.10m, reopened.Lines[0].DiscountShare);
    }

    [Fact]
    public async Task An_edit_that_lowers_the_net_below_the_saved_discount_is_refused_and_the_database_is_unchanged()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var creator = NewService(out var creatorContext);
        await using var _c = creatorContext;
        var created = (await creator.CreateAsync(Input(customer, Line(product, 2m)) with { Discount = 150m }, default)).Value;

        var editor = NewService(out var editContext);
        await using var _e = editContext;
        var refused = await editor.UpdateAsync(
            created.Id,
            Input(customer, new InvoiceLineInput(created.Lines[0].Id.ToString(), null, 1m)) with { Discount = 150m },
            default);

        Assert.True(refused.IsFailure);
        Assert.Equal(["Discount cannot be more than the total net of the lines."], refused.Error!.Errors!["discount"]);

        var reader = NewService(out var readContext);
        await using var _r = readContext;
        var reopened = (await reader.GetAsync(created.Id, default)).Value;
        Assert.Equal(150m, reopened.Discount);
        Assert.Equal(2m, reopened.Lines[0].Quantity);
        Assert.Equal(created.Totals, reopened.Totals);
    }

    // ---- amounts stay true when products change (spec 006, story 3) ----

    [Fact]
    public async Task Amounts_do_not_follow_the_product_when_it_is_repriced_and_deactivated_through_the_real_product_service_and_a_new_quantity_uses_the_saved_price()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var creator = NewService(out var creatorContext);
        await using var _c = creatorContext;
        var created = (await creator.CreateAsync(Input(customer, Line(product, 2m)) with { Discount = 20m }, default)).Value;
        Assert.Equal(207.00m, created.Totals.GrandTotal);

        await using (var productContext = database.CreateContext())
        {
            var products = new ProductService(new EfProductStore(productContext), TimeProvider.System);
            Assert.True((await products.UpdateAsync(product.Id, new ProductInput("Consulting day", Unique("N"), "Service", 999.99m, 0m), default)).IsSuccess);
            Assert.True((await products.SetActiveAsync(product.Id, new ProductActiveRequest(false), default)).IsSuccess);
        }

        var reader = NewService(out var readContext);
        await using var _r = readContext;
        var reopened = (await reader.GetAsync(created.Id, default)).Value;
        Assert.Equal(created.Totals, reopened.Totals);
        Assert.Equal(created.Lines[0].Net, reopened.Lines[0].Net);
        Assert.Equal(created.Lines[0].Vat, reopened.Lines[0].Vat);

        var editor = NewService(out var editContext);
        await using var _e = editContext;
        var edited = await editor.UpdateAsync(
            created.Id,
            Input(customer, new InvoiceLineInput(created.Lines[0].Id.ToString(), null, 3m)) with { Discount = 20m },
            default);
        Assert.True(edited.IsSuccess);
        Assert.Equal(300.00m, edited.Value.Totals.TotalNet);
        Assert.Equal(280.00m, edited.Value.Totals.NetAfterDiscount);
        Assert.Equal(42.00m, edited.Value.Totals.TotalVat);
        Assert.Equal(322.00m, edited.Value.Totals.GrandTotal);
    }

    [Fact]
    public async Task A_discount_equal_to_the_net_and_a_too_large_invoice_are_handled_on_real_SQL_and_a_refusal_saves_nothing()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var huge = await AddProductAsync("Huge price", 999_999_999.99m, 15m);
        var before = await CountInvoicesAsync();

        var service = NewService(out var context);
        await using var _ = context;
        var whole = await service.CreateAsync(Input(customer, Line(product, 2m)) with { Discount = 200m }, default);
        var refused = await service.CreateAsync(Input(customer, Line(huge, 999_999.99m)), default);

        Assert.True(whole.IsSuccess);
        Assert.Equal(0.00m, whole.Value.Totals.GrandTotal);
        Assert.True(refused.IsFailure);
        Assert.Equal(["The amount of this line is too large. Reduce the quantity."], refused.Error!.Errors!["lines[0].quantity"]);
        Assert.Equal(before + 1, await CountInvoicesAsync());
    }
}
