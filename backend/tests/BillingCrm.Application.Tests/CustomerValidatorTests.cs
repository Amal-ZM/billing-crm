using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class CustomerValidatorTests
{
    private static CustomerInput Valid() => new(
        Name: "Acme Trading",
        Type: "Company",
        Email: "accounts@acme.example",
        Phone: "+966 11 555 0100",
        VatNumber: "300000000000003",
        Address: "12 King Fahd Rd, Riyadh");

    private static IReadOnlyDictionary<string, string[]> ErrorsOf(CustomerInput input)
    {
        var result = CustomerValidator.Validate(input);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    [Fact]
    public void A_valid_input_passes_and_is_converted()
    {
        var result = CustomerValidator.Validate(Valid());

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Trading", result.Value.Name);
        Assert.Equal(CustomerType.Company, result.Value.Type);
        Assert.Equal("accounts@acme.example", result.Value.Email);
        Assert.Equal("300000000000003", result.Value.VatNumber);
    }

    [Fact]
    public void Every_text_field_is_trimmed()
    {
        var result = CustomerValidator.Validate(new CustomerInput("  Acme  ", " Individual ", "  A@B.CO  ", "  0500000000 ", "  V-1 ", "  Somewhere  "));

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme", result.Value.Name);
        Assert.Equal(CustomerType.Individual, result.Value.Type);
        Assert.Equal("A@B.CO", result.Value.Email);
        Assert.Equal("0500000000", result.Value.Phone);
        Assert.Equal("V-1", result.Value.VatNumber);
        Assert.Equal("Somewhere", result.Value.Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_vat_number_means_none(string? vat)
    {
        var result = CustomerValidator.Validate(Valid() with { VatNumber = vat });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VatNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name) => Assert.Contains("name", ErrorsOf(Valid() with { Name = name }).Keys);

    [Fact]
    public void Name_may_be_exactly_200_characters_but_not_201()
    {
        Assert.True(CustomerValidator.Validate(Valid() with { Name = new string('a', 200) }).IsSuccess);
        Assert.Contains("name", ErrorsOf(Valid() with { Name = new string('a', 201) }).Keys);
    }

    [Fact]
    public void A_name_containing_a_bidi_override_character_is_refused_on_the_name_field()
    {
        var errors = ErrorsOf(Valid() with { Name = "Acme‮Trading" });

        Assert.Contains("name", errors.Keys);
        Assert.Contains("Name contains a character that is not allowed.", errors["name"]);
    }

    [Fact]
    public void An_address_containing_a_bidi_override_character_is_refused_on_the_address_field()
    {
        var errors = ErrorsOf(Valid() with { Address = "Riyadh‮Street" });

        Assert.Contains("address", errors.Keys);
        Assert.Contains("Address contains a character that is not allowed.", errors["address"]);
    }

    [Fact]
    public void Arabic_and_mixed_names_are_accepted_unchanged()
    {
        var result = CustomerValidator.Validate(Valid() with { Name = "شركة الأمل Al-Amal Co." });

        Assert.True(result.IsSuccess);
        Assert.Equal("شركة الأمل Al-Amal Co.", result.Value.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Partnership")]
    [InlineData("1")]
    [InlineData("0")]
    public void Type_is_required_and_must_be_one_of_the_two_names(string? type) =>
        Assert.Contains("type", ErrorsOf(Valid() with { Type = type }).Keys);

    [Theory]
    [InlineData("Company", CustomerType.Company)]
    [InlineData("company", CustomerType.Company)]
    [InlineData("INDIVIDUAL", CustomerType.Individual)]
    public void Type_is_matched_ignoring_case(string type, CustomerType expected)
    {
        var result = CustomerValidator.Validate(Valid() with { Type = type });

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign.example")]
    [InlineData("two@@example.com")]
    [InlineData("a@b@example.com")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@nodot")]
    [InlineData("user@.example.com")]
    [InlineData("user@example.")]
    [InlineData("us er@example.com")]
    [InlineData("user@exa mple.com")]
    public void Email_must_be_present_and_shaped_like_an_address(string? email) =>
        Assert.Contains("email", ErrorsOf(Valid() with { Email = email }).Keys);

    [Fact]
    public void Email_may_be_exactly_256_characters_but_not_257()
    {
        var local = new string('a', 256 - "@x.co".Length);
        Assert.True(CustomerValidator.Validate(Valid() with { Email = local + "@x.co" }).IsSuccess);
        Assert.Contains("email", ErrorsOf(Valid() with { Email = local + "a@x.co" }).Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456")] // 6 characters
    [InlineData("123456789012345678901")] // 21 characters
    [InlineData("0500-abc-1234")]
    [InlineData("0500#123456")]
    public void Phone_must_be_present_7_to_20_characters_from_the_allowed_set(string? phone) =>
        Assert.Contains("phone", ErrorsOf(Valid() with { Phone = phone }).Keys);

    [Theory]
    [InlineData("1234567")]
    [InlineData("12345678901234567890")]
    [InlineData("+966 (11) 555-0100")]
    public void Phone_accepts_digits_spaces_plus_minus_and_parentheses(string phone) =>
        Assert.True(CustomerValidator.Validate(Valid() with { Phone = phone }).IsSuccess);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Address_is_required(string? address) => Assert.Contains("address", ErrorsOf(Valid() with { Address = address }).Keys);

    [Fact]
    public void Address_may_be_exactly_500_characters_but_not_501_and_may_span_lines()
    {
        Assert.True(CustomerValidator.Validate(Valid() with { Address = new string('a', 500) }).IsSuccess);
        Assert.Contains("address", ErrorsOf(Valid() with { Address = new string('a', 501) }).Keys);
        Assert.True(CustomerValidator.Validate(Valid() with { Address = "Line 1\nLine 2" }).IsSuccess);
    }

    [Fact]
    public void Vat_number_may_be_exactly_30_characters_but_not_31()
    {
        Assert.True(CustomerValidator.Validate(Valid() with { VatNumber = new string('9', 30) }).IsSuccess);
        Assert.Contains("vatNumber", ErrorsOf(Valid() with { VatNumber = new string('9', 31) }).Keys);
    }

    [Theory]
    [InlineData("VAT#123")]
    [InlineData("VAT/123")]
    [InlineData("VAT_123")]
    public void Vat_number_rejects_characters_other_than_letters_digits_spaces_and_hyphens(string vat) =>
        Assert.Contains("vatNumber", ErrorsOf(Valid() with { VatNumber = vat }).Keys);

    [Fact]
    public void Vat_number_accepts_letters_digits_spaces_and_hyphens()
    {
        Assert.True(CustomerValidator.Validate(Valid() with { VatNumber = "GB 123-456 AB" }).IsSuccess);
    }

    [Fact]
    public void Several_bad_fields_are_all_reported_together_with_camel_case_keys()
    {
        var errors = ErrorsOf(new CustomerInput(null, "Nope", "bad", "1", "#", null));

        Assert.Equal(
            new[] { "address", "email", "name", "phone", "type", "vatNumber" },
            errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.All(errors.Values, messages => Assert.NotEmpty(messages));
    }
}
