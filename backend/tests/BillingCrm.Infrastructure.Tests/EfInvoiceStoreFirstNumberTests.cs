using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// The very first invoice on a brand-new database is INV-000001. This class has exactly one test on purpose: it has its own
/// fixture (so its own fresh database and sequence), and a second test in the class could take the first number first.
/// </summary>
public class EfInvoiceStoreFirstNumberTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_first_invoice_on_a_fresh_database_is_number_1_shown_as_INV_000001()
    {
        var customer = Customer.Create("ABC", CustomerType.Company, "first@example.test", "0500000000", null, "Riyadh", Now);
        var product = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        }

        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfProductStore(context).TryAddAsync(product, default));
        }

        var invoice = Invoice.Create(customer.Id, new DateOnly(2030, 7, 1), null, Now);
        invoice.AddLine(product, 2m, Now);
        await using (var context = database.CreateContext())
        {
            await new EfInvoiceStore(context).AddAsync(invoice, default);
        }

        Assert.Equal(1, invoice.Number);
        Assert.Equal("INV-000001", InvoiceNumber.Format(invoice.Number));
    }
}
