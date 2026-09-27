using BillingCrm.Application.Customers;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

public class EfCustomerStoreCreateTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Customer NewCustomer(string email, string name = "Acme Trading", string address = "12 King Fahd Rd") =>
        Customer.Create(name, CustomerType.Company, email, "+966 11 555 0100", null, address, Now);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task TryAdd_persists_every_field_including_arabic_text_and_a_multi_line_address()
    {
        var customer = Customer.Create("شركة الأمل", CustomerType.Individual, Unique("arabic"), "0500000000", "VAT-123", "الرياض\nحي النخيل", Now);

        await using (var context = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        }

        await using var read = database.CreateContext();
        var loaded = await new EfCustomerStore(read).FindByIdAsync(customer.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("شركة الأمل", loaded.Name);
        Assert.Equal(CustomerType.Individual, loaded.Type);
        Assert.Equal(customer.Email, loaded.Email);
        Assert.Equal(customer.NormalizedEmail, loaded.NormalizedEmail);
        Assert.Equal("0500000000", loaded.Phone);
        Assert.Equal("VAT-123", loaded.VatNumber);
        Assert.Equal("الرياض\nحي النخيل", loaded.Address);
        Assert.True(loaded.IsActive);
        Assert.Equal(Now, loaded.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal(Now, loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task A_missing_vat_number_is_stored_as_null()
    {
        var customer = NewCustomer(Unique("novat"));

        await using var context = database.CreateContext();
        await new EfCustomerStore(context).TryAddAsync(customer, default);

        await using var read = database.CreateContext();
        Assert.Null((await new EfCustomerStore(read).FindByIdAsync(customer.Id, default))!.VatNumber);
    }

    [Fact]
    public async Task A_second_add_with_the_same_normalized_email_returns_false_and_stores_nothing()
    {
        var email = Unique("dup");

        await using var first = database.CreateContext();
        Assert.True(await new EfCustomerStore(first).TryAddAsync(NewCustomer(email), default));

        var duplicate = NewCustomer(email.ToUpperInvariant(), name: "Someone Else");
        await using var second = database.CreateContext();
        Assert.False(await new EfCustomerStore(second).TryAddAsync(duplicate, default));

        await using var read = database.CreateContext();
        Assert.Equal(1, await read.Customers.CountAsync(customer => customer.NormalizedEmail == duplicate.NormalizedEmail));
        Assert.Null(await new EfCustomerStore(read).FindByIdAsync(duplicate.Id, default));
    }

    [Fact]
    public async Task The_store_stays_usable_after_a_refused_duplicate()
    {
        var email = Unique("reuse");
        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        Assert.True(await store.TryAddAsync(NewCustomer(email), default));
        Assert.False(await store.TryAddAsync(NewCustomer(email, name: "Dup"), default));

        Assert.True(await store.TryAddAsync(NewCustomer(Unique("after")), default));
    }

    [Fact]
    public async Task The_unique_index_itself_rejects_a_duplicate_even_when_the_pre_check_is_bypassed()
    {
        var email = Unique("race").ToUpperInvariant();
        await using var context = database.CreateContext();
        const string insert =
            "INSERT INTO Customers (Id, Name, Type, Email, NormalizedEmail, Phone, Address, IsActive, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (@id, N'A', 'Company', @email, @email, '0500000000', N'x', 1, SYSUTCDATETIME(), SYSUTCDATETIME())";

        await context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("email", email));
        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync(insert, new SqlParameter("id", Guid.NewGuid()), new SqlParameter("email", email)));

        // An index (not a constraint) is what EF creates, so SQL Server reports 2601.
        Assert.Equal(2601, exception.Number);
    }

    [Fact]
    public async Task The_type_check_constraint_rejects_an_invalid_value()
    {
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Customers (Id, Name, Type, Email, NormalizedEmail, Phone, Address, IsActive, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (NEWID(), N'A', 'Partnership', @e, @e, '0500000000', N'x', 1, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new SqlParameter("e", Unique("check"))));

        Assert.Equal(547, exception.Number);
    }

    [Fact]
    public async Task EmailExists_finds_by_normalized_email_and_can_exclude_one_customer()
    {
        var customer = NewCustomer(Unique("exists"));
        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        await store.TryAddAsync(customer, default);

        Assert.True(await store.EmailExistsAsync(customer.NormalizedEmail, null, default));
        Assert.False(await store.EmailExistsAsync(customer.NormalizedEmail, customer.Id, default));
        Assert.False(await store.EmailExistsAsync(EmailNormalizer.Normalize(Unique("other")), null, default));
    }

    [Fact]
    public async Task Search_with_default_paging_orders_by_name_then_id_and_counts_everything()
    {
        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        var marker = Guid.NewGuid().ToString("N")[..8];
        foreach (var name in new[] { "Zeta", "Alpha", "Mike" })
        {
            await store.TryAddAsync(NewCustomer(Unique(name.ToLowerInvariant()), name: $"{name} {marker}"), default);
        }

        var page = await store.SearchAsync(new CustomerSearch(null, null, null, 1, 100), default);

        var mine = page.Items.Where(customer => customer.Name.EndsWith(marker, StringComparison.Ordinal)).Select(customer => customer.Name).ToList();
        Assert.Equal(new[] { $"Alpha {marker}", $"Mike {marker}", $"Zeta {marker}" }, mine);
        Assert.True(page.TotalCount >= 3);
    }
}
