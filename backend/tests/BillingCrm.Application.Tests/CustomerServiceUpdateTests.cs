using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Tests.Fakes;

namespace BillingCrm.Application.Tests;

public class CustomerServiceUpdateTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeCustomerStore _store = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly CustomerService _sut;

    public CustomerServiceUpdateTests()
    {
        _sut = new CustomerService(_store, _time);
    }

    private static CustomerInput Input(string name = "Acme Trading", string email = "accounts@acme.example", string phone = "0500000000") =>
        new(name, "Company", email, phone, null, "12 King Fahd Rd");

    private async Task<CustomerDto> CreateAsync(CustomerInput input) =>
        (await _sut.CreateAsync(input, CancellationToken.None)).Value;

    [Fact]
    public async Task A_valid_update_changes_the_fields_and_moves_the_changed_time_but_not_the_created_time()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(2));

        var result = await _sut.UpdateAsync(
            created.Id,
            new CustomerInput("  Acme Group  ", "Individual", "accounts@acme.example", "0511111111", " VAT-1 ", "New Address"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Group", result.Value.Name);
        Assert.Equal("Individual", result.Value.Type);
        Assert.Equal("0511111111", result.Value.Phone);
        Assert.Equal("VAT-1", result.Value.VatNumber);
        Assert.Equal("New Address", result.Value.Address);
        Assert.Equal(Start.UtcDateTime, result.Value.CreatedAtUtc);
        Assert.Equal(Start.AddHours(2).UtcDateTime, result.Value.UpdatedAtUtc);
        Assert.Equal(1, _store.TryUpdateCalls);
    }

    [Theory]
    [InlineData("accounts@acme.example")]
    [InlineData("ACCOUNTS@ACME.EXAMPLE")]
    [InlineData("  Accounts@Acme.example ")]
    public async Task Saving_a_customer_with_its_own_email_is_not_a_duplicate(string ownEmail)
    {
        var created = await CreateAsync(Input());

        var result = await _sut.UpdateAsync(created.Id, Input(name: "Renamed", email: ownEmail), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", result.Value.Name);
    }

    [Theory]
    [InlineData("other@acme.example")]
    [InlineData("OTHER@ACME.EXAMPLE")]
    [InlineData("  Other@Acme.Example  ")]
    public async Task Another_customers_email_is_a_conflict_and_leaves_the_customer_unchanged(string othersEmail)
    {
        await CreateAsync(Input(email: "other@acme.example", name: "Other Co"));
        var mine = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(1));

        var result = await _sut.UpdateAsync(mine.Id, Input(name: "Changed", email: othersEmail), CancellationToken.None);

        Assert.Equal(CustomerService.EmailInUse, result.Error);
        var stored = (await _sut.GetAsync(mine.Id, CancellationToken.None)).Value;
        Assert.Equal("Acme Trading", stored.Name);
        Assert.Equal("accounts@acme.example", stored.Email);
        Assert.Equal(mine.UpdatedAtUtc, stored.UpdatedAtUtc);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task Losing_the_race_at_the_database_on_update_gives_the_same_conflict()
    {
        var created = await CreateAsync(Input());
        _store.TryUpdateLosesRace = true;

        var result = await _sut.UpdateAsync(created.Id, Input(name: "Changed"), CancellationToken.None);

        Assert.Equal(CustomerService.EmailInUse, result.Error);
        Assert.Equal(1, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task Saving_with_nothing_changed_leaves_the_changed_time_alone_and_does_not_write()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromDays(1));

        var result = await _sut.UpdateAsync(created.Id, Input(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.UpdatedAtUtc, result.Value.UpdatedAtUtc);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task Validation_applies_as_on_create_and_changes_nothing()
    {
        var created = await CreateAsync(Input());

        var result = await _sut.UpdateAsync(created.Id, Input(phone: "1") with { Name = "" }, CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(new[] { "name", "phone" }, result.Error.Errors!.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal("Acme Trading", (await _sut.GetAsync(created.Id, CancellationToken.None)).Value.Name);
        Assert.Equal(0, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task Updating_an_unknown_customer_is_not_found()
    {
        var result = await _sut.UpdateAsync(Guid.NewGuid(), Input(), CancellationToken.None);

        Assert.Equal(CustomerService.NotFound, result.Error);
    }

    [Fact]
    public async Task An_unknown_customer_is_reported_as_not_found_even_when_the_input_is_invalid()
    {
        var result = await _sut.UpdateAsync(Guid.NewGuid(), new CustomerInput(null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(CustomerService.NotFound, result.Error);
    }

    [Fact]
    public async Task Deactivating_keeps_every_field_and_moves_the_changed_time()
    {
        var created = await CreateAsync(Input());
        _time.Advance(TimeSpan.FromHours(3));

        var result = await _sut.SetActiveAsync(created.Id, new SetActiveRequest(false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
        Assert.Equal(created with { IsActive = false, UpdatedAtUtc = Start.AddHours(3).UtcDateTime }, result.Value);
    }

    [Fact]
    public async Task Reactivating_makes_the_customer_active_again()
    {
        var created = await CreateAsync(Input());
        await _sut.SetActiveAsync(created.Id, new SetActiveRequest(false), CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));

        var result = await _sut.SetActiveAsync(created.Id, new SetActiveRequest(true), CancellationToken.None);

        Assert.True(result.Value.IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Setting_the_state_it_already_has_changes_nothing_including_the_changed_time(bool alreadyIs)
    {
        var created = await CreateAsync(Input());
        if (!alreadyIs)
        {
            await _sut.SetActiveAsync(created.Id, new SetActiveRequest(false), CancellationToken.None);
        }

        var before = (await _sut.GetAsync(created.Id, CancellationToken.None)).Value;
        var callsBefore = _store.TryUpdateCalls;
        _time.Advance(TimeSpan.FromDays(1));

        var result = await _sut.SetActiveAsync(created.Id, new SetActiveRequest(alreadyIs), CancellationToken.None);

        Assert.Equal(before, result.Value);
        Assert.Equal(callsBefore, _store.TryUpdateCalls);
    }

    [Fact]
    public async Task A_missing_is_active_is_a_validation_failure_naming_isActive()
    {
        var created = await CreateAsync(Input());

        var result = await _sut.SetActiveAsync(created.Id, new SetActiveRequest(null), CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("isActive", result.Error.Errors!.Keys);
    }

    [Fact]
    public async Task Changing_the_active_state_of_an_unknown_customer_is_not_found()
    {
        var result = await _sut.SetActiveAsync(Guid.NewGuid(), new SetActiveRequest(false), CancellationToken.None);

        Assert.Equal(CustomerService.NotFound, result.Error);
    }
}
