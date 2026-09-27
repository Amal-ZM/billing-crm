using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

public class EfInvoiceStoreCreateTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Due = new(2030, 7, 1);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Customer> AddCustomerAsync(string name = "ABC")
    {
        var customer = Customer.Create(name, CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync(string name = "Consulting hour", decimal price = 100m, decimal vat = 15m, ProductUnit unit = ProductUnit.Hour)
    {
        var product = Product.Create(name, Unique("P"), unit, price, vat, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private async Task<Invoice> AddInvoiceAsync(Customer customer, string? notes, params (Product Product, decimal Quantity)[] lines)
    {
        var invoice = Invoice.Create(customer.Id, Due, notes, Now);
        foreach (var (product, quantity) in lines)
        {
            invoice.AddLine(product, quantity, Now);
        }

        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).AddAsync(invoice, default);
        return invoice;
    }

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return (await new EfInvoiceStore(context).FindByIdAsync(id, default))!;
    }

    // ---- persistence ----

    [Fact]
    public async Task An_invoice_at_the_limits_of_100_lines_and_a_1000_character_note_is_saved_and_read_back_whole()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var notes = new string('ن', 1000);

        var saved = await AddInvoiceAsync(customer, notes, Enumerable.Range(0, 100).Select(_ => (product, 1m)).ToArray());

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal(1000, loaded.Notes!.Length);
        Assert.Equal(notes, loaded.Notes);
        Assert.Equal(100, loaded.Lines.Count);
        Assert.Equal(Enumerable.Range(1, 100), loaded.Lines.Select(line => line.Position));
    }

    [Fact]
    public async Task AddAsync_persists_the_invoice_and_every_line_field_including_arabic_text_and_multi_line_notes()
    {
        var customer = await AddCustomerAsync("شركة الأمل");
        var product = await AddProductAsync("ساعة استشارة", 250.75m, 5m, ProductUnit.Service);

        var saved = await AddInvoiceAsync(customer, "ملاحظات\nالسطر الثاني", (product, 2m));

        var loaded = await ReloadAsync(saved.Id);
        Assert.True(loaded.Number >= 1);
        Assert.Equal(saved.Number, loaded.Number);
        Assert.Equal(customer.Id, loaded.CustomerId);
        Assert.Equal(InvoiceStatus.Draft, loaded.Status);
        Assert.Equal(Due, loaded.DueDate);
        Assert.Equal("ملاحظات\nالسطر الثاني", loaded.Notes);
        Assert.Equal(Now, loaded.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal(Now, loaded.UpdatedAtUtc);
        var line = Assert.Single(loaded.Lines);
        Assert.Equal(loaded.Id, line.InvoiceId);
        Assert.Equal(1, line.Position);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal("ساعة استشارة", line.ProductName);
        Assert.Equal(product.Sku, line.ProductSku);
        Assert.Equal(ProductUnit.Service, line.Unit);
        Assert.Equal(250.75m, line.UnitPrice);
        Assert.Equal(5m, line.VatPercent);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public async Task Notes_are_stored_as_null_when_there_are_none()
    {
        var saved = await AddInvoiceAsync(await AddCustomerAsync(), null, (await AddProductAsync(), 1m));

        Assert.Null((await ReloadAsync(saved.Id)).Notes);
    }

    [Fact]
    public async Task Lines_come_back_in_the_order_they_were_added()
    {
        var customer = await AddCustomerAsync();
        var first = await AddProductAsync("Zeta");
        var second = await AddProductAsync("Alpha");
        var third = await AddProductAsync("Mike");

        var saved = await AddInvoiceAsync(customer, null, (first, 1m), (second, 2m), (third, 3m));

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal(["Zeta", "Alpha", "Mike"], loaded.Lines.Select(line => line.ProductName));
        Assert.Equal([1, 2, 3], loaded.Lines.Select(line => line.Position));
        Assert.Equal([1m, 2m, 3m], loaded.Lines.Select(line => line.Quantity));
    }

    [Theory]
    [InlineData("100.10", "15.00", "1.50")]
    [InlineData("999999999.99", "100.00", "999999.99")]
    [InlineData("0.01", "0.00", "0.01")]
    [InlineData("0.00", "12.50", "10.00")]
    [InlineData("5000.50", "7.25", "3.25")]
    public async Task Price_vat_and_quantity_round_trip_as_exactly_the_same_decimals(string price, string vat, string quantity)
    {
        var p = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        var v = decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture);
        var q = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);
        var product = await AddProductAsync(price: p, vat: v);

        var saved = await AddInvoiceAsync(await AddCustomerAsync(), null, (product, q));

        var line = Assert.Single((await ReloadAsync(saved.Id)).Lines);
        Assert.Equal(p, line.UnitPrice);
        Assert.Equal(v, line.VatPercent);
        Assert.Equal(q, line.Quantity);
    }

    // ---- the number sequence ----

    [Fact]
    public async Task Each_invoice_gets_a_number_from_the_sequence_and_the_next_one_is_higher()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();

        var first = await AddInvoiceAsync(customer, null, (product, 1m));
        var second = await AddInvoiceAsync(customer, null, (product, 1m));

        Assert.True(first.Number >= 1);
        Assert.True(second.Number > first.Number);
        Assert.Equal("INV-" + first.Number.ToString().PadLeft(6, '0'), InvoiceNumber.Format(first.Number));
    }

    [Fact]
    public async Task Fifty_invoices_created_at_the_same_moment_all_get_different_numbers_and_none_is_refused()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();

        var numbers = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            await Task.Yield();
            var invoice = Invoice.Create(customer.Id, Due, null, Now);
            invoice.AddLine(product, 1m, Now);
            await using var context = database.CreateContext();
            await new EfInvoiceStore(context).AddAsync(invoice, default);
            return invoice.Number;
        }));

        Assert.Equal(50, numbers.Distinct().Count());
        Assert.All(numbers, number => Assert.True(number >= 1));
    }

    [Fact]
    public async Task The_unique_index_rejects_a_duplicate_number_even_when_it_is_set_by_hand()
    {
        var customer = await AddCustomerAsync();
        var number = Random.Shared.Next(900_000, 999_000);
        await using var context = database.CreateContext();
        const string insert =
            "INSERT INTO Invoices (Id, Number, CustomerId, Status, DueDate, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (@id, @number, @customer, 'Draft', '2030-07-01', SYSUTCDATETIME(), SYSUTCDATETIME())";

        await context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("number", number), new SqlParameter("customer", customer.Id));
        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("number", number), new SqlParameter("customer", customer.Id)));

        Assert.Equal(2601, exception.Number);
        Assert.Contains("IX_Invoices_Number", exception.Message);
    }

    // ---- constraints ----

    private async Task<Guid> RawInvoiceAsync()
    {
        var customer = await AddCustomerAsync();
        var invoice = await AddInvoiceAsync(customer, null, (await AddProductAsync(), 1m));
        return invoice.Id;
    }

    [Theory]
    [InlineData("0", "10", "15", "'Item'")]
    [InlineData("-1", "10", "15", "'Item'")]
    [InlineData("1000000", "10", "15", "'Item'")]
    [InlineData("1", "-0.01", "15", "'Item'")]
    [InlineData("1", "10", "100.01", "'Item'")]
    [InlineData("1", "10", "-1", "'Item'")]
    [InlineData("1", "10", "15", "'Box'")]
    public async Task The_check_constraints_reject_a_bad_quantity_price_vat_or_unit_written_directly(string quantity, string price, string vat, string unit)
    {
        var invoiceId = await RawInvoiceAsync();
        var product = await AddProductAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO InvoiceLines (Id, InvoiceId, Position, ProductId, ProductName, ProductSku, Unit, UnitPrice, VatPercent, Quantity) " +
            $"VALUES (NEWID(), @invoice, 99, @product, N'X', 'X-1', {unit}, {price}, {vat}, {quantity})",
            new SqlParameter("invoice", invoiceId),
            new SqlParameter("product", product.Id)));

        Assert.Equal(547, exception.Number);
    }

    [Theory]
    [InlineData("Overdue")]
    [InlineData("Archived")]
    [InlineData("sent ")]
    [InlineData("")]
    public async Task The_status_check_constraint_rejects_any_status_outside_the_four_stored_ones_and_Overdue_is_not_stored(string status)
    {
        var customer = await AddCustomerAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, CustomerId, Status, DueDate, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (NEWID(), @customer, @status, '2030-07-01', SYSUTCDATETIME(), SYSUTCDATETIME())",
            new SqlParameter("customer", customer.Id),
            new SqlParameter("status", status)));

        // Several checks reject such a row (the status list, and the two that tie times and frozen amounts to the status); SQL Server
        // names whichever it evaluates first, so the exact list is proven by the smoke test on the constraint's definition.
        Assert.Equal(547, exception.Number);
        Assert.Contains("CK_Invoices_", exception.Message);
    }

    [Fact]
    public async Task The_foreign_keys_reject_an_unknown_customer_and_an_unknown_product()
    {
        var invoiceId = await RawInvoiceAsync();
        await using var context = database.CreateContext();

        var badCustomer = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, CustomerId, Status, DueDate, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (NEWID(), NEWID(), 'Draft', '2030-07-01', SYSUTCDATETIME(), SYSUTCDATETIME())"));
        var badProduct = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO InvoiceLines (Id, InvoiceId, Position, ProductId, ProductName, ProductSku, Unit, UnitPrice, VatPercent, Quantity) " +
            "VALUES (NEWID(), @invoice, 98, NEWID(), N'X', 'X-1', 'Item', 1, 15, 1)",
            new SqlParameter("invoice", invoiceId)));

        Assert.Equal(547, badCustomer.Number);
        Assert.Equal(547, badProduct.Number);
    }

    [Fact]
    public async Task A_customer_or_product_that_an_invoice_uses_cannot_be_deleted()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        await AddInvoiceAsync(customer, null, (product, 1m));
        await using var context = database.CreateContext();

        var deleteCustomer = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM Customers WHERE Id = @id", new SqlParameter("id", customer.Id)));
        var deleteProduct = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM Products WHERE Id = @id", new SqlParameter("id", product.Id)));

        Assert.Equal(547, deleteCustomer.Number);
        Assert.Equal(547, deleteProduct.Number);
    }

    // ---- the snapshot ----

    [Fact]
    public async Task A_line_shows_its_own_saved_columns_after_the_product_row_is_changed_directly()
    {
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var saved = await AddInvoiceAsync(await AddCustomerAsync(), null, (product, 2m));

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Products SET Name = N'Renamed', Sku = @sku, Unit = 'Service', UnitPrice = 999.99, VatPercent = 0, IsActive = 0 WHERE Id = @id",
                new SqlParameter("sku", Unique("NEW")),
                new SqlParameter("id", product.Id));
        }

        var line = Assert.Single((await ReloadAsync(saved.Id)).Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal(product.Sku, line.ProductSku);
        Assert.Equal(ProductUnit.Hour, line.Unit);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);
    }

    [Fact]
    public async Task There_is_no_live_total_amount_or_balance_in_what_is_stored_only_frozen_copies()
    {
        var product = await AddProductAsync(price: 100m, vat: 15m);
        var saved = await AddInvoiceAsync(await AddCustomerAsync(), null, (product, 2m));
        await using var context = database.CreateContext();

        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('Invoices', 'InvoiceLines')")
            .ToListAsync();

        Assert.NotEmpty(columns);
        // The only stored amounts are the Frozen* copies written when an invoice leaves Draft (spec 007); no balance is stored at all.
        Assert.DoesNotContain(columns, column => !column.StartsWith("Frozen", StringComparison.Ordinal)
            && (column.Contains("total", StringComparison.OrdinalIgnoreCase) || column.Contains("amount", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(columns, column => column.Contains("balance", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(0, saved.Number);
    }
}
