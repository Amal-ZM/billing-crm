using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// The discount column and the amounts on real SQL Server. Amounts are not stored: an invoice made before spec 006, whose row has
/// no discount in it, must still read back with a discount of 0 and correct amounts (SC-006, no backfill).
/// </summary>
public class EfInvoiceAmountsTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Customer> AddCustomerAsync()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync(decimal price = 100m, decimal vat = 15m)
    {
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, price, vat, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    /// <summary>An invoice and one line written the way spec 005 wrote them: no <c>Discount</c> anywhere in the insert.</summary>
    private async Task<Guid> InsertOldStyleInvoiceAsync(Customer customer, Product product, decimal quantity)
    {
        var invoiceId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, CustomerId, Status, DueDate, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (@id, @customer, 'Draft', '2030-07-01', SYSUTCDATETIME(), SYSUTCDATETIME())",
            new SqlParameter("id", invoiceId),
            new SqlParameter("customer", customer.Id));
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO InvoiceLines (Id, InvoiceId, Position, ProductId, ProductName, ProductSku, Unit, UnitPrice, VatPercent, Quantity) " +
            "VALUES (NEWID(), @invoice, 1, @product, @name, @sku, 'Hour', @price, @vat, @quantity)",
            new SqlParameter("invoice", invoiceId),
            new SqlParameter("product", product.Id),
            new SqlParameter("name", product.Name),
            new SqlParameter("sku", product.Sku),
            new SqlParameter("price", product.UnitPrice),
            new SqlParameter("vat", product.VatPercent),
            new SqlParameter("quantity", quantity));
        return invoiceId;
    }

    [Fact]
    public async Task An_invoice_made_the_005_way_reads_back_with_no_discount_and_correct_amounts_without_any_backfill()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync(100m, 15m);
        var id = await InsertOldStyleInvoiceAsync(customer, product, 2m);

        await using var context = database.CreateContext();
        var invoice = (await new EfInvoiceStore(context).FindByIdAsync(id, default))!;

        Assert.Equal(0m, invoice.Discount);
        Assert.Equal(200.00m, invoice.Amounts.TotalNet);
        Assert.Equal(30.00m, invoice.Amounts.TotalVat);
        Assert.Equal(230.00m, invoice.Amounts.GrandTotal);
    }

    [Fact]
    public async Task The_service_returns_the_amounts_of_an_old_invoice_straight_away()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync(100m, 15m);
        var id = await InsertOldStyleInvoiceAsync(customer, product, 2m);

        await using var context = database.CreateContext();
        var service = new InvoiceService(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser());
        var result = await service.GetAsync(id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.Discount);
        Assert.Equal(230.00m, result.Value.Totals.GrandTotal);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(200.00m, line.Net);
        Assert.Equal(30.00m, line.Vat);
    }

    [Fact]
    public async Task A_discount_of_20_10_is_saved_and_read_back_exactly_with_consistent_amounts()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var invoice = Invoice.Create(customer.Id, new DateOnly(2030, 7, 1), null, Now, 20.10m);
        invoice.AddLine(product, 2m, Now);
        await using (var context = database.CreateContext())
        {
            await new EfInvoiceStore(context).AddAsync(invoice, default);
        }

        await using var reader = database.CreateContext();
        var loaded = (await new EfInvoiceStore(reader).FindByIdAsync(invoice.Id, default))!;

        Assert.Equal(20.10m, loaded.Discount);
        Assert.Equal(179.90m, loaded.Amounts.NetAfterDiscount);
        Assert.Equal(206.89m, loaded.Amounts.GrandTotal);
    }

    [Fact]
    public async Task A_changed_discount_on_a_loaded_invoice_is_saved_and_clearing_it_saves_zero()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var invoice = Invoice.Create(customer.Id, new DateOnly(2030, 7, 1), null, Now, 20.00m);
        invoice.AddLine(product, 2m, Now);
        await using (var context = database.CreateContext())
        {
            await new EfInvoiceStore(context).AddAsync(invoice, default);
        }

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var loaded = (await store.FindByIdAsync(invoice.Id, default))!;
            Assert.True(loaded.UpdateDetails(loaded.CustomerId, loaded.DueDate, loaded.Notes, 0m, Now.AddHours(1)));
            Assert.True(await store.SaveChangesAsync(default));
        }

        await using var reader = database.CreateContext();
        Assert.Equal(0m, (await new EfInvoiceStore(reader).FindByIdAsync(invoice.Id, default))!.Discount);
    }

    [Fact]
    public async Task The_check_constraint_rejects_a_negative_discount_written_directly()
    {
        var customer = await AddCustomerAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, CustomerId, Status, DueDate, Discount, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (NEWID(), @customer, 'Draft', '2030-07-01', -0.01, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new SqlParameter("customer", customer.Id)));

        Assert.Equal(547, exception.Number);
        Assert.Contains("CK_Invoices_Discount", exception.Message);
    }

    [Fact]
    public async Task A_normally_added_invoice_has_a_discount_of_zero_in_the_database()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync();
        var invoice = Invoice.Create(customer.Id, new DateOnly(2030, 7, 1), null, Now);
        invoice.AddLine(product, 2m, Now);
        await using (var context = database.CreateContext())
        {
            await new EfInvoiceStore(context).AddAsync(invoice, default);
        }

        await using var reader = database.CreateContext();
        var stored = (await reader.Database
            .SqlQueryRaw<decimal>("SELECT Discount AS [Value] FROM Invoices WHERE Id = {0}", invoice.Id)
            .ToListAsync()).Single();
        Assert.Equal(0.00m, stored);
    }

    [Fact]
    public async Task A_saved_discount_above_the_net_left_by_an_older_version_still_reads_and_shows_the_whole_net()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync(100m, 15m);
        var id = await InsertOldStyleInvoiceAsync(customer, product, 0.30m);
        await using (var writer = database.CreateContext())
        {
            await writer.Database.ExecuteSqlRawAsync("UPDATE Invoices SET Discount = 50.00 WHERE Id = {0}", id);
        }

        await using var context = database.CreateContext();
        var service = new InvoiceService(new EfInvoiceStore(context), new EfCustomerStore(context), new EfProductStore(context), TimeProvider.System, new FakeCurrentUser());
        var result = await service.GetAsync(id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(30.00m, result.Value.Totals.TotalNet);
        Assert.Equal(30.00m, result.Value.Discount);
        Assert.Equal(0.00m, result.Value.Totals.GrandTotal);
    }
}
