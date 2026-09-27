using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// How an edited Draft is saved on real SQL Server: a tracked invoice is loaded, changed through the aggregate (lines added,
/// changed and removed) and saved with one call. These prove that a new line is inserted (not mistaken for an existing row),
/// a removed line is deleted, and that the whole save is all-or-nothing.
/// </summary>
public class EfInvoiceStoreUpdateTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(2);
    private static readonly DateOnly Due = new(2030, 7, 1);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Customer> AddCustomerAsync(string name = "ABC")
    {
        var customer = Customer.Create(name, CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync(string name, decimal price = 100m, decimal vat = 15m)
    {
        var product = Product.Create(name, Unique("P"), ProductUnit.Hour, price, vat, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private async Task<Invoice> AddInvoiceAsync(Customer customer, params (Product Product, decimal Quantity)[] lines)
    {
        var invoice = Invoice.Create(customer.Id, Due, "Net 30", Now);
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

    private async Task<int> LineRowsAsync(Guid invoiceId)
    {
        await using var context = database.CreateContext();
        return (await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM InvoiceLines WHERE InvoiceId = {0}", invoiceId)
            .ToListAsync()).Single();
    }

    [Fact]
    public async Task FindById_returns_the_invoice_with_its_lines_in_position_order_and_null_for_an_unknown_id()
    {
        var customer = await AddCustomerAsync();
        var saved = await AddInvoiceAsync(customer, (await AddProductAsync("Zeta"), 1m), (await AddProductAsync("Alpha"), 2m));

        var loaded = await ReloadAsync(saved.Id);

        Assert.Equal(["Zeta", "Alpha"], loaded.Lines.Select(line => line.ProductName));
        await using var context = database.CreateContext();
        Assert.Null(await new EfInvoiceStore(context).FindByIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_new_line_added_to_a_loaded_invoice_is_inserted_with_a_copy_of_the_product_as_it_is_then()
    {
        var customer = await AddCustomerAsync();
        var first = await AddProductAsync("Consulting hour", 100m, 15m);
        var second = await AddProductAsync("Website build", 5000.50m, 0m);
        var saved = await AddInvoiceAsync(customer, (first, 2m));

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var invoice = (await store.FindByIdAsync(saved.Id, default))!;
            invoice.AddLine(second, 1.5m, Later);
            await store.SaveChangesAsync(default);
        }

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal(2, loaded.Lines.Count);
        Assert.Equal(2, await LineRowsAsync(saved.Id));
        var added = loaded.Lines[1];
        Assert.Equal(2, added.Position);
        Assert.Equal("Website build", added.ProductName);
        Assert.Equal(5000.50m, added.UnitPrice);
        Assert.Equal(0m, added.VatPercent);
        Assert.Equal(1.5m, added.Quantity);
        Assert.Equal(Later, loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task A_removed_line_is_deleted_from_the_table()
    {
        var customer = await AddCustomerAsync();
        var saved = await AddInvoiceAsync(customer, (await AddProductAsync("One"), 1m), (await AddProductAsync("Two"), 2m), (await AddProductAsync("Three"), 3m));
        Assert.Equal(3, await LineRowsAsync(saved.Id));

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var invoice = (await store.FindByIdAsync(saved.Id, default))!;
            Assert.True(invoice.RemoveLine(invoice.Lines[1].Id, Later));
            await store.SaveChangesAsync(default);
        }

        Assert.Equal(2, await LineRowsAsync(saved.Id));
        Assert.Equal(["One", "Three"], (await ReloadAsync(saved.Id)).Lines.Select(line => line.ProductName));
    }

    [Fact]
    public async Task A_kept_lines_quantity_is_updated_and_its_saved_copy_is_untouched_even_though_the_product_changed()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour", 100m, 15m);
        var saved = await AddInvoiceAsync(customer, (product, 2m));
        await using (var productContext = database.CreateContext())
        {
            await productContext.Database.ExecuteSqlRawAsync(
                "UPDATE Products SET Name = N'Renamed', UnitPrice = 999.99, VatPercent = 0 WHERE Id = @id", new SqlParameter("id", product.Id));
        }

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var invoice = (await store.FindByIdAsync(saved.Id, default))!;
            Assert.True(invoice.SetLineQuantity(invoice.Lines[0].Id, 7.25m, Later));
            await store.SaveChangesAsync(default);
        }

        var line = Assert.Single((await ReloadAsync(saved.Id)).Lines);
        Assert.Equal(7.25m, line.Quantity);
        Assert.Equal("Consulting hour", line.ProductName);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(15m, line.VatPercent);
    }

    [Fact]
    public async Task The_header_changes_persist_the_customer_the_due_date_the_notes_and_the_changed_time()
    {
        var customer = await AddCustomerAsync("ABC");
        var other = await AddCustomerAsync("Other Co");
        var saved = await AddInvoiceAsync(customer, (await AddProductAsync("Consulting hour"), 2m));

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var invoice = (await store.FindByIdAsync(saved.Id, default))!;
            Assert.True(invoice.UpdateDetails(other.Id, new DateOnly(2030, 9, 15), "ملاحظات\nجديدة", invoice.Discount, Later));
            await store.SaveChangesAsync(default);
        }

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal(other.Id, loaded.CustomerId);
        Assert.Equal(new DateOnly(2030, 9, 15), loaded.DueDate);
        Assert.Equal("ملاحظات\nجديدة", loaded.Notes);
        Assert.Equal(Later, loaded.UpdatedAtUtc);
        Assert.Equal(Now, loaded.CreatedAtUtc);
        Assert.Equal(saved.Number, loaded.Number);
    }

    [Fact]
    public async Task Adding_removing_and_changing_lines_in_one_save_all_happen_together()
    {
        var customer = await AddCustomerAsync();
        var one = await AddProductAsync("One");
        var two = await AddProductAsync("Two");
        var three = await AddProductAsync("Three");
        var saved = await AddInvoiceAsync(customer, (one, 1m), (two, 2m));

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var invoice = (await store.FindByIdAsync(saved.Id, default))!;
            var lastId = invoice.Lines[1].Id;
            invoice.SetLineQuantity(invoice.Lines[0].Id, 10m, Later);
            invoice.RemoveLine(lastId, Later);
            // The removed line held the highest position; the new line takes the position after the highest that remains.
            invoice.AddLine(three, 3m, Later);
            await store.SaveChangesAsync(default);
        }

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal(["One", "Three"], loaded.Lines.Select(line => line.ProductName));
        Assert.Equal([10m, 3m], loaded.Lines.Select(line => line.Quantity));
        Assert.Equal([1, 2], loaded.Lines.Select(line => line.Position));
        Assert.Equal(2, await LineRowsAsync(saved.Id));
    }

    [Fact]
    public async Task A_save_that_breaks_a_constraint_saves_nothing_at_all()
    {
        var customer = await AddCustomerAsync();
        var product = await AddProductAsync("Consulting hour");
        var saved = await AddInvoiceAsync(customer, (product, 2m));

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            var invoice = (await store.FindByIdAsync(saved.Id, default))!;
            invoice.UpdateDetails(invoice.CustomerId, Due, "should not be saved", invoice.Discount, Later);
            invoice.AddLine(product, 0m, Later);

            await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(default));
        }

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal("Net 30", loaded.Notes);
        Assert.Equal(Now, loaded.UpdatedAtUtc);
        Assert.Single(loaded.Lines);
        Assert.Equal(1, await LineRowsAsync(saved.Id));
    }

    [Fact]
    public async Task A_save_that_lost_a_race_with_another_edit_reports_false_and_writes_nothing()
    {
        var customer = await AddCustomerAsync();
        var first = await AddProductAsync("One");
        var second = await AddProductAsync("Two");
        var saved = await AddInvoiceAsync(customer, (first, 2m), (second, 3m));
        var lineId = saved.Lines.First().Id;

        // Two people open the same Draft. The first removes a line and saves; the second, still holding the old copy,
        // changes that very line's quantity (and the notes) and saves.
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstStore = new EfInvoiceStore(firstContext);
        var secondStore = new EfInvoiceStore(secondContext);
        var firstCopy = (await firstStore.FindByIdAsync(saved.Id, default))!;
        var secondCopy = (await secondStore.FindByIdAsync(saved.Id, default))!;

        firstCopy.RemoveLine(lineId, Later);
        Assert.True(await firstStore.SaveChangesAsync(default));

        secondCopy.UpdateDetails(secondCopy.CustomerId, Due, "should not be saved", secondCopy.Discount, Later);
        secondCopy.SetLineQuantity(lineId, 9m, Later);
        Assert.False(await secondStore.SaveChangesAsync(default));

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal("Net 30", loaded.Notes);
        Assert.Equal(["Two"], loaded.Lines.Select(line => line.ProductName));
    }

    [Fact]
    public async Task Saving_a_loaded_invoice_that_was_not_changed_writes_nothing()
    {
        var customer = await AddCustomerAsync();
        var saved = await AddInvoiceAsync(customer, (await AddProductAsync("Consulting hour"), 2m));

        await using (var context = database.CreateContext())
        {
            var store = new EfInvoiceStore(context);
            await store.FindByIdAsync(saved.Id, default);
            await store.SaveChangesAsync(default);
        }

        var loaded = await ReloadAsync(saved.Id);
        Assert.Equal(Now, loaded.UpdatedAtUtc);
        Assert.Single(loaded.Lines);
    }
}
