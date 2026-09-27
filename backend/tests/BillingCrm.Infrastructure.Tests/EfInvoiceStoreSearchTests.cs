using BillingCrm.Application.Invoices;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// The invoice list on real SQL Server: order, paging, the joined customer name. The database is shared by the tests in this class,
/// so each test looks only at its own invoices (found by id) or at the whole list where the property is about everything.
/// </summary>
public class EfInvoiceStoreSearchTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private async Task<Customer> AddCustomerAsync(string name)
    {
        var customer = Customer.Create(name, CustomerType.Company, $"{Guid.NewGuid():N}@example.test", "0500000000", null, "Riyadh", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Product> AddProductAsync()
    {
        var product = Product.Create("Consulting hour", Unique("P"), ProductUnit.Hour, 100m, 15m, Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        return product;
    }

    private async Task<Invoice> AddInvoiceAsync(Customer customer, Product product, DateTime createdAtUtc)
    {
        var invoice = Invoice.Create(customer.Id, new DateOnly(2030, 7, 1), null, createdAtUtc);
        invoice.AddLine(product, 1m, createdAtUtc);
        await using var context = database.CreateContext();
        await new EfInvoiceStore(context).AddAsync(invoice, default);
        return invoice;
    }

    private async Task<BillingCrm.Application.Common.PagedResult<BillingCrm.Application.Invoices.InvoiceListRow>> SearchAsync(int page, int pageSize)
    {
        await using var context = database.CreateContext();
        return await new EfInvoiceStore(context).SearchAsync(InvoiceSearchFilter.None, DateOnly.FromDateTime(DateTime.UtcNow), page, pageSize, default);
    }

    [Fact]
    public async Task Invoices_come_newest_first_by_created_time_then_by_number()
    {
        var customer = await AddCustomerAsync("ABC");
        var product = await AddProductAsync();
        var far = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(1, 500_000));
        var older = await AddInvoiceAsync(customer, product, far);
        var newer = await AddInvoiceAsync(customer, product, far.AddMinutes(1));
        var sameTimeFirst = await AddInvoiceAsync(customer, product, far.AddMinutes(2));
        var sameTimeSecond = await AddInvoiceAsync(customer, product, far.AddMinutes(2));

        var page = await SearchAsync(1, 100);

        var mine = page.Items.Where(row => new[] { older.Id, newer.Id, sameTimeFirst.Id, sameTimeSecond.Id }.Contains(row.Id)).Select(row => row.Id).ToList();
        Assert.Equal([sameTimeSecond.Id, sameTimeFirst.Id, newer.Id, older.Id], mine);
    }

    [Fact]
    public async Task Each_row_carries_the_number_status_due_date_created_time_and_the_customers_current_name_including_arabic()
    {
        var customer = await AddCustomerAsync("شركة الأمل");
        var product = await AddProductAsync();
        var saved = await AddInvoiceAsync(customer, product, new DateTime(2099, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(1, 500_000)));

        var row = (await SearchAsync(1, 100)).Items.Single(candidate => candidate.Id == saved.Id);

        Assert.Equal(saved.Number, row.Number);
        Assert.Equal(InvoiceStatus.Draft, row.Status);
        Assert.Equal(customer.Id, row.CustomerId);
        Assert.Equal("شركة الأمل", row.CustomerName);
        Assert.Equal(new DateOnly(2030, 7, 1), row.DueDate);
        Assert.Equal(saved.CreatedAtUtc, row.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, row.CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task The_customer_name_is_read_at_list_time_so_a_rename_shows_up()
    {
        var customer = await AddCustomerAsync("Before");
        var product = await AddProductAsync();
        var saved = await AddInvoiceAsync(customer, product, new DateTime(2099, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(1, 500_000)));
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE Customers SET Name = N'After' WHERE Id = {0}", customer.Id);
        }

        var row = (await SearchAsync(1, 100)).Items.Single(candidate => candidate.Id == saved.Id);

        Assert.Equal("After", row.CustomerName);
    }

    [Fact]
    public async Task Paging_is_stable_no_invoice_appears_twice_or_is_skipped_and_the_totals_add_up()
    {
        var customer = await AddCustomerAsync("ABC");
        var product = await AddProductAsync();
        var sameMoment = new DateTime(2098, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var made = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            made.Add((await AddInvoiceAsync(customer, product, sameMoment)).Id);
        }

        var seen = new List<Guid>();
        var first = await SearchAsync(1, 3);
        var totalPages = first.TotalPages;
        for (var page = 1; page <= totalPages; page++)
        {
            seen.AddRange((await SearchAsync(page, 3)).Items.Select(row => row.Id));
        }

        Assert.Equal(first.TotalCount, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Empty(made.Except(seen));
        Assert.Equal((first.TotalCount + 2) / 3, totalPages);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    public async Task A_page_beyond_the_end_is_empty_and_reports_the_real_total(int page)
    {
        var customer = await AddCustomerAsync("ABC");
        await AddInvoiceAsync(customer, await AddProductAsync(), Now);

        var result = await SearchAsync(page, 100);

        Assert.Empty(result.Items);
        Assert.True(result.TotalCount >= 1);
        Assert.Equal((result.TotalCount + 99) / 100, result.TotalPages);
    }
}
