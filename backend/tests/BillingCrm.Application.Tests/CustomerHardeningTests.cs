using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Tests.Fakes;

namespace BillingCrm.Application.Tests;

/// <summary>Follow-ups from the spec 003 reviews: huge values, message text, and deliberate choices.</summary>
public class CustomerHardeningTests
{
    private readonly FakeCustomerStore _store = new();
    private readonly CustomerService _sut;

    public CustomerHardeningTests()
    {
        _sut = new CustomerService(_store, new FixedTimeProvider(new DateTimeOffset(2030, 6, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    private static CustomerInput Valid() => new("Acme Trading", "Company", "a@acme.example", "0500000000", null, "Somewhere");

    private static string MessageFor(CustomerInput input, string field)
    {
        var result = CustomerValidator.Validate(input);
        Assert.True(result.IsFailure);
        return Assert.Single(result.Error!.Errors![field]);
    }

    [Fact]
    public async Task The_largest_possible_page_number_is_accepted_and_passed_on_without_overflowing()
    {
        var result = await _sut.SearchAsync(new CustomerQuery(null, null, null, int.MaxValue.ToString(), "100"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(int.MaxValue, _store.LastSearch!.Page);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task A_page_number_too_big_for_an_int_is_a_validation_failure_not_a_crash()
    {
        var result = await _sut.SearchAsync(new CustomerQuery(null, null, null, "99999999999999999999", null), CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("page", result.Error.Errors!.Keys);
    }

    [Fact]
    public void Total_pages_is_computed_without_floating_point_or_overflow()
    {
        var result = PagedResult<int>.Create([], 1, 1, int.MaxValue);

        Assert.Equal(int.MaxValue, result.TotalPages);
        Assert.Equal(3, PagedResult<int>.Create([], 1, 2, 5).TotalPages);
        Assert.Equal(1, PagedResult<int>.Create([], 1, 20, 20).TotalPages);
        Assert.Equal(2, PagedResult<int>.Create([], 1, 20, 21).TotalPages);
        Assert.Equal(0, PagedResult<int>.Create([], 1, 20, 0).TotalPages);
    }

    [Fact]
    public async Task Search_text_of_exactly_100_characters_is_accepted_but_101_is_a_validation_failure_naming_q()
    {
        var ok = await _sut.SearchAsync(new CustomerQuery(new string('a', CustomerService.MaxSearchLength), null, null, null, null), CancellationToken.None);
        var tooLong = await _sut.SearchAsync(new CustomerQuery(new string('a', CustomerService.MaxSearchLength + 1), null, null, null, null), CancellationToken.None);

        Assert.True(ok.IsSuccess);
        Assert.Equal(ErrorKind.Validation, tooLong.Error!.Kind);
        Assert.Contains("q", tooLong.Error.Errors!.Keys);
    }

    [Fact]
    public async Task Surrounding_spaces_do_not_count_towards_the_search_text_limit()
    {
        var padded = "  " + new string('a', CustomerService.MaxSearchLength) + "  ";

        var result = await _sut.SearchAsync(new CustomerQuery(padded, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Over_length_messages_name_the_field_and_its_maximum()
    {
        Assert.Equal("Name must be at most 200 characters.", MessageFor(Valid() with { Name = new string('a', 201) }, "name"));
        Assert.Equal("Email must be at most 256 characters.", MessageFor(Valid() with { Email = new string('a', 251) + "@x.com" }, "email"));
        Assert.Equal("Address must be at most 500 characters.", MessageFor(Valid() with { Address = new string('a', 501) }, "address"));
        Assert.Equal("VAT number must be at most 30 characters.", MessageFor(Valid() with { VatNumber = new string('9', 31) }, "vatNumber"));
        Assert.Equal("Phone must be 7 to 20 characters.", MessageFor(Valid() with { Phone = new string('1', 21) }, "phone"));
    }

    [Fact]
    public void Length_limits_count_the_trimmed_text_so_padding_cannot_cause_a_refusal()
    {
        var paddedName = "  " + new string('a', 200) + "  ";

        Assert.True(CustomerValidator.Validate(Valid() with { Name = paddedName }).IsSuccess);
    }

    [Fact]
    public void Phone_numbers_typed_with_arabic_indic_digits_are_accepted_by_design()
    {
        // char.IsDigit is deliberate: staff may type Arabic-Indic digits. Such a number is stored as typed
        // (no normalisation), and a search for the ASCII form will not match it; that is a documented limit.
        var result = CustomerValidator.Validate(Valid() with { Phone = "٠٥٠٠٠٠٠٠٠٠" });

        Assert.True(result.IsSuccess);
        Assert.Equal("٠٥٠٠٠٠٠٠٠٠", result.Value.Phone);
    }
}
