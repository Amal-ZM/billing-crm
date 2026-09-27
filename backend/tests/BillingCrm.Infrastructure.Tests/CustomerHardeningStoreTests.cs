using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// The spec 003 edge case "two staff save the same email at the same moment", proven with the real
/// CustomerService on the real store and database, plus the other follow-ups from the reviews.
/// </summary>
public class CustomerHardeningStoreTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>A service with its own context, exactly as one API request would have.</summary>
    private async Task<T> WithServiceAsync<T>(Func<CustomerService, Task<T>> action)
    {
        await using var context = database.CreateContext();
        return await action(new CustomerService(new EfCustomerStore(context), TimeProvider.System));
    }

    private static CustomerInput Input(string email, string name = "Acme Trading") =>
        new(name, "Company", email, "0500000000", null, "Somewhere");

    [Fact]
    public async Task Many_simultaneous_adds_of_the_same_email_produce_exactly_one_customer_and_all_the_rest_are_409()
    {
        var email = Unique("race");
        using var start = new ManualResetEventSlim(false);

        var attempts = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            start.Wait();
            return await WithServiceAsync(service => service.CreateAsync(Input(i % 2 == 0 ? email : email.ToUpperInvariant(), $"Racer {i}"), default));
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.All(results.Where(result => result.IsFailure), result => Assert.Equal(CustomerService.EmailInUse, result.Error));

        await using var read = database.CreateContext();
        Assert.Equal(1, await read.Customers.CountAsync(customer => customer.NormalizedEmail == EmailNormalizer.Normalize(email)));
    }

    [Fact]
    public async Task Two_simultaneous_edits_to_the_same_new_email_let_exactly_one_win_and_the_other_customer_keeps_its_email()
    {
        var first = (await WithServiceAsync(service => service.CreateAsync(Input(Unique("a")), default))).Value;
        var second = (await WithServiceAsync(service => service.CreateAsync(Input(Unique("b")), default))).Value;
        var contested = Unique("contested");
        using var start = new ManualResetEventSlim(false);

        var attempts = new[] { first, second }.Select(customer => Task.Run(async () =>
        {
            start.Wait();
            return await WithServiceAsync(service => service.UpdateAsync(customer.Id, Input(contested, customer.Name), default));
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.Single(results, result => result.IsFailure && result.Error == CustomerService.EmailInUse);

        await using var read = database.CreateContext();
        var stored = await read.Customers.Where(customer => customer.Id == first.Id || customer.Id == second.Id).ToListAsync();
        Assert.Single(stored, customer => customer.NormalizedEmail == EmailNormalizer.Normalize(contested));
        Assert.Equal(2, stored.Select(customer => customer.NormalizedEmail).Distinct().Count());
    }

    [Fact]
    public async Task A_violation_of_some_other_unique_constraint_is_not_reported_as_email_in_use()
    {
        var existing = Customer.Create("Existing", CustomerType.Company, Unique("pk"), "0500000000", null, "Somewhere", Now);
        await using (var setup = database.CreateContext())
        {
            Assert.True(await new EfCustomerStore(setup).TryAddAsync(existing, default));
        }

        // Same primary key, different email: the database refuses it, but not because of the email index.
        var clash = Customer.Create("Clash", CustomerType.Company, Unique("pk2"), "0500000000", null, "Somewhere", Now);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(clash, existing.Id);

        await using var context = database.CreateContext();
        await Assert.ThrowsAsync<DbUpdateException>(() => new EfCustomerStore(context).TryAddAsync(clash, default));
    }

    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(21_474_838, 100)]
    [InlineData(int.MaxValue, 1)]
    public async Task A_huge_page_number_is_an_empty_page_with_the_real_total_not_a_database_error(int page, int pageSize)
    {
        await using (var seed = database.CreateContext())
        {
            await new EfCustomerStore(seed).TryAddAsync(
                Customer.Create("Overflow Probe", CustomerType.Company, Unique("overflow"), "0500000000", null, "Somewhere", Now), default);
        }

        await using var context = database.CreateContext();
        var result = await new EfCustomerStore(context).SearchAsync(new CustomerSearch(null, null, null, page, pageSize), default);

        Assert.Empty(result.Items);
        Assert.True(result.TotalCount >= 1);
        Assert.Equal(page, result.Page);
    }
}
