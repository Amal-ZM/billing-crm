using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class CustomerServiceCreateTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeCustomerStore _store = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly CustomerService _sut;

    public CustomerServiceCreateTests()
    {
        _sut = new CustomerService(_store, _time);
    }

    private static CustomerInput Input(string email = "accounts@acme.example", string name = "Acme Trading") => new(
        Name: name,
        Type: "Company",
        Email: email,
        Phone: "+966 11 555 0100",
        VatNumber: null,
        Address: "12 King Fahd Rd");

    [Fact]
    public async Task Creates_an_active_customer_with_equal_timestamps_and_trimmed_values()
    {
        var result = await _sut.CreateAsync(Input(name: "  Acme Trading  ", email: "  Accounts@Acme.example "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var customer = result.Value;
        Assert.Equal("Acme Trading", customer.Name);
        Assert.Equal("Accounts@Acme.example", customer.Email);
        Assert.Equal("Company", customer.Type);
        Assert.True(customer.IsActive);
        Assert.Null(customer.VatNumber);
        Assert.Equal(Now.UtcDateTime, customer.CreatedAtUtc);
        Assert.Equal(customer.CreatedAtUtc, customer.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Single(_store.All);
    }

    [Theory]
    [InlineData("accounts@acme.example")]
    [InlineData("ACCOUNTS@ACME.EXAMPLE")]
    [InlineData("  Accounts@Acme.Example  ")]
    public async Task A_duplicate_email_is_a_conflict_whatever_the_case_or_spacing_and_creates_nothing(string duplicate)
    {
        await _sut.CreateAsync(Input(), CancellationToken.None);

        var second = await _sut.CreateAsync(Input(email: duplicate, name: "Someone Else"), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
        Assert.Equal("Email already in use", second.Error.Title);
        Assert.Equal("Another customer already uses this email address.", second.Error.Detail);
        Assert.Single(_store.All);
    }

    [Fact]
    public async Task Losing_the_race_at_the_database_gives_the_same_conflict()
    {
        _store.TryAddLosesRace = true;

        var result = await _sut.CreateAsync(Input(), CancellationToken.None);

        Assert.Equal(CustomerService.EmailInUse, result.Error);
        Assert.Equal(1, _store.TryAddCalls);
        Assert.Empty(_store.All);
    }

    [Fact]
    public async Task A_validation_failure_reaches_neither_the_lookup_nor_the_store()
    {
        var result = await _sut.CreateAsync(Input() with { Phone = "1" }, CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(0, _store.TryAddCalls);
        Assert.Empty(_store.All);
    }

    [Fact]
    public async Task An_inactive_customer_still_reserves_its_email()
    {
        var created = await _sut.CreateAsync(Input(), CancellationToken.None);
        _store.All.Single().SetActive(false, Now.UtcDateTime);

        var second = await _sut.CreateAsync(Input(name: "Other"), CancellationToken.None);

        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
        Assert.True(created.IsSuccess);
    }

    [Fact]
    public async Task Get_returns_the_customer()
    {
        var created = await _sut.CreateAsync(Input(), CancellationToken.None);

        var result = await _sut.GetAsync(created.Value.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Value, result.Value);
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_not_found()
    {
        var result = await _sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal("Customer not found", result.Error.Title);
    }

    [Fact]
    public async Task Listing_with_no_query_uses_page_1_of_20_and_reports_totals()
    {
        for (var i = 0; i < 25; i++)
        {
            await _sut.CreateAsync(Input(email: $"c{i:00}@example.test", name: $"Customer {i:00}"), CancellationToken.None);
        }

        var result = await _sut.SearchAsync(new CustomerQuery(null, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Equal(25, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
        Assert.Equal(20, result.Value.Items.Count);
        Assert.Equal("Customer 00", result.Value.Items[0].Name);
    }
}
