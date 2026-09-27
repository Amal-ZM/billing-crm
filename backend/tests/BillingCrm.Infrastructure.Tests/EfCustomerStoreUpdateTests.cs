using BillingCrm.Application.Customers;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;

namespace BillingCrm.Infrastructure.Tests;

public class EfCustomerStoreUpdateTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(2);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private async Task<Customer> AddAsync(string email, string name = "Acme Trading")
    {
        var customer = Customer.Create(name, CustomerType.Company, email, "0500000000", null, "12 King Fahd Rd", Now);
        await using var context = database.CreateContext();
        Assert.True(await new EfCustomerStore(context).TryAddAsync(customer, default));
        return customer;
    }

    private async Task<Customer> LoadAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return (await new EfCustomerStore(context).FindByIdAsync(id, default))!;
    }

    [Fact]
    public async Task TryUpdate_persists_changes_including_arabic_text()
    {
        var added = await AddAsync(Unique("update"));

        await using (var context = database.CreateContext())
        {
            var store = new EfCustomerStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            Assert.True(tracked.Update("شركة الأمل", CustomerType.Individual, tracked.Email, "0511111111", "VAT-7", "الرياض\nحي النخيل", Later));
            Assert.True(await store.TryUpdateAsync(tracked, default));
        }

        var loaded = await LoadAsync(added.Id);
        Assert.Equal("شركة الأمل", loaded.Name);
        Assert.Equal(CustomerType.Individual, loaded.Type);
        Assert.Equal("0511111111", loaded.Phone);
        Assert.Equal("VAT-7", loaded.VatNumber);
        Assert.Equal("الرياض\nحي النخيل", loaded.Address);
        Assert.Equal(Later, loaded.UpdatedAtUtc);
        Assert.Equal(Now, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Taking_another_customers_normalized_email_returns_false_and_the_stored_row_keeps_its_old_email()
    {
        var other = await AddAsync(Unique("other"), name: "Other");
        var mine = await AddAsync(Unique("mine"));

        await using (var context = database.CreateContext())
        {
            var store = new EfCustomerStore(context);
            var tracked = (await store.FindByIdAsync(mine.Id, default))!;
            tracked.Update("Changed Name", CustomerType.Company, other.Email.ToUpperInvariant(), tracked.Phone, null, tracked.Address, Later);

            Assert.False(await store.TryUpdateAsync(tracked, default));

            // The rejected edit is forgotten, so this context does not go on believing it happened.
            Assert.Equal(mine.Email, tracked.Email);
            Assert.Equal("Acme Trading", tracked.Name);
        }

        var stored = await LoadAsync(mine.Id);
        Assert.Equal(mine.Email, stored.Email);
        Assert.Equal("Acme Trading", stored.Name);
        Assert.Equal(Now, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task The_store_stays_usable_after_a_refused_update()
    {
        var other = await AddAsync(Unique("other2"), name: "Other");
        var mine = await AddAsync(Unique("mine2"));

        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        var tracked = (await store.FindByIdAsync(mine.Id, default))!;
        tracked.Update("Changed", CustomerType.Company, other.Email, tracked.Phone, null, tracked.Address, Later);
        Assert.False(await store.TryUpdateAsync(tracked, default));

        tracked.Update("Changed again", CustomerType.Company, tracked.Email, tracked.Phone, null, tracked.Address, Later);
        Assert.True(await store.TryUpdateAsync(tracked, default));
        Assert.Equal("Changed again", (await LoadAsync(mine.Id)).Name);
    }

    [Fact]
    public async Task Saving_a_customer_with_its_own_email_in_a_different_case_succeeds()
    {
        var added = await AddAsync(Unique("case"));

        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        var tracked = (await store.FindByIdAsync(added.Id, default))!;
        tracked.Update(tracked.Name, tracked.Type, tracked.Email.ToUpperInvariant(), tracked.Phone, null, tracked.Address, Later);

        Assert.True(await store.TryUpdateAsync(tracked, default));
        Assert.Equal(added.Email.ToUpperInvariant(), (await LoadAsync(added.Id)).Email);
    }

    [Fact]
    public async Task Deactivating_keeps_the_row_and_every_field_and_the_email_stays_reserved()
    {
        var added = await AddAsync(Unique("dormant"));

        await using (var context = database.CreateContext())
        {
            var store = new EfCustomerStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            Assert.True(tracked.SetActive(false, Later));
            Assert.True(await store.TryUpdateAsync(tracked, default));
        }

        var loaded = await LoadAsync(added.Id);
        Assert.False(loaded.IsActive);
        Assert.Equal(added.Name, loaded.Name);
        Assert.Equal(added.Email, loaded.Email);
        Assert.Equal(added.Phone, loaded.Phone);
        Assert.Equal(added.Address, loaded.Address);

        var newcomer = Customer.Create("Newcomer", CustomerType.Individual, added.Email, "0500000001", null, "Elsewhere", Later);
        await using var second = database.CreateContext();
        Assert.False(await new EfCustomerStore(second).TryAddAsync(newcomer, default));
    }

    [Fact]
    public async Task An_inactive_customer_is_still_found_by_search_and_email_lookup()
    {
        var added = await AddAsync(Unique("findme"), name: $"Findme {Guid.NewGuid():N}");
        await using (var context = database.CreateContext())
        {
            var store = new EfCustomerStore(context);
            var tracked = (await store.FindByIdAsync(added.Id, default))!;
            tracked.SetActive(false, Later);
            await store.TryUpdateAsync(tracked, default);
        }

        await using var read = database.CreateContext();
        var store2 = new EfCustomerStore(read);
        var found = await store2.SearchAsync(new CustomerSearch(added.Name, null, null, 1, 10), default);

        Assert.Single(found.Items);
        Assert.False(found.Items[0].IsActive);
        Assert.True(await store2.EmailExistsAsync(added.NormalizedEmail, null, default));
    }
}
