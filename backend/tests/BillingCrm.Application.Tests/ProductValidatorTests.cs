using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class ProductValidatorTests
{
    private static ProductInput Valid() => new(
        Name: "Consulting hour",
        Sku: "CNS-001",
        Unit: "Hour",
        UnitPrice: 100m,
        VatPercent: 15m);

    private static IReadOnlyDictionary<string, string[]> ErrorsOf(ProductInput input, bool defaultVat = true)
    {
        var result = ProductValidator.Validate(input, defaultVat);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    [Fact]
    public void A_valid_input_passes_and_is_converted()
    {
        var result = ProductValidator.Validate(Valid(), defaultVat: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Consulting hour", result.Value.Name);
        Assert.Equal("CNS-001", result.Value.Sku);
        Assert.Equal(ProductUnit.Hour, result.Value.Unit);
        Assert.Equal(100m, result.Value.UnitPrice);
        Assert.Equal(15m, result.Value.VatPercent);
    }

    [Fact]
    public void Text_is_trimmed_and_the_sku_is_upper_cased()
    {
        var result = ProductValidator.Validate(new ProductInput("  Consulting hour  ", "  cns-001 ", " service ", 1m, 15m), defaultVat: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Consulting hour", result.Value.Name);
        Assert.Equal("CNS-001", result.Value.Sku);
        Assert.Equal(ProductUnit.Service, result.Value.Unit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string? name)
    {
        Assert.True(ErrorsOf(Valid() with { Name = name }).ContainsKey("name"));
    }

    [Fact]
    public void A_name_of_exactly_200_characters_is_accepted_and_201_is_refused()
    {
        Assert.True(ProductValidator.Validate(Valid() with { Name = new string('x', 200) }, true).IsSuccess);
        Assert.True(ErrorsOf(Valid() with { Name = new string('x', 201) }).ContainsKey("name"));
    }

    [Fact]
    public void A_name_containing_a_bidi_override_character_is_refused_on_the_name_field()
    {
        var errors = ErrorsOf(Valid() with { Name = "Consulting‮Hour" });

        Assert.Contains("name", errors.Keys);
        Assert.Contains("Name contains a character that is not allowed.", errors["name"]);
    }

    [Fact]
    public void An_arabic_name_is_accepted()
    {
        Assert.True(ProductValidator.Validate(Valid() with { Name = "ساعة استشارة" }, true).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_sku_is_required(string? sku)
    {
        Assert.True(ErrorsOf(Valid() with { Sku = sku }).ContainsKey("sku"));
    }

    [Fact]
    public void A_sku_of_exactly_50_characters_is_accepted_and_51_is_refused()
    {
        Assert.True(ProductValidator.Validate(Valid() with { Sku = new string('A', 50) }, true).IsSuccess);
        Assert.True(ErrorsOf(Valid() with { Sku = new string('A', 51) }).ContainsKey("sku"));
    }

    [Theory]
    [InlineData("AB CD")]
    [InlineData("AB#1")]
    [InlineData("AB.1")]
    [InlineData("AB/1")]
    [InlineData("ABé1")]
    [InlineData("رمز١")]
    [InlineData("AB‏1")]
    public void A_sku_with_other_characters_is_refused_including_non_ascii_letters(string sku)
    {
        Assert.True(ErrorsOf(Valid() with { Sku = sku }).ContainsKey("sku"));
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("cns-001", "CNS-001")]
    [InlineData("cns_001", "CNS_001")]
    [InlineData("0-9_a-Z", "0-9_A-Z")]
    public void Letters_digits_hyphens_and_underscores_are_accepted_and_upper_cased(string sku, string expected)
    {
        var result = ProductValidator.Validate(Valid() with { Sku = sku }, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Sku);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Box")]
    [InlineData("1")]
    [InlineData("0")]
    public void A_unit_is_required_and_must_be_one_of_the_three_names(string? unit)
    {
        Assert.True(ErrorsOf(Valid() with { Unit = unit }).ContainsKey("unit"));
    }

    [Theory]
    [InlineData("Hour", ProductUnit.Hour)]
    [InlineData("hour", ProductUnit.Hour)]
    [InlineData("ITEM", ProductUnit.Item)]
    [InlineData("Service", ProductUnit.Service)]
    public void A_unit_is_accepted_in_any_letter_case(string unit, ProductUnit expected)
    {
        var result = ProductValidator.Validate(Valid() with { Unit = unit }, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Unit);
    }

    [Fact]
    public void A_unit_price_is_required()
    {
        Assert.True(ErrorsOf(Valid() with { UnitPrice = null }).ContainsKey("unitPrice"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("100")]
    [InlineData("100.5")]
    [InlineData("100.50")]
    [InlineData("100.500")]
    [InlineData("999999999.99")]
    public void A_price_within_range_and_two_decimals_is_accepted_and_kept_exactly(string text)
    {
        var price = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var result = ProductValidator.Validate(Valid() with { UnitPrice = price }, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(price, result.Value.UnitPrice);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-1")]
    [InlineData("1000000000")]
    [InlineData("999999999.995")]
    public void A_price_out_of_range_is_refused(string text)
    {
        var price = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(ErrorsOf(Valid() with { UnitPrice = price }).ContainsKey("unitPrice"));
    }

    [Theory]
    [InlineData("10.005")]
    [InlineData("0.001")]
    [InlineData("99.999")]
    public void A_price_with_more_than_two_decimals_is_refused_never_rounded(string text)
    {
        var price = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var errors = ErrorsOf(Valid() with { UnitPrice = price });

        Assert.Contains("two decimal", errors["unitPrice"][0]);
    }

    [Fact]
    public void Money_values_come_back_exactly_as_given()
    {
        var result = ProductValidator.Validate(Valid() with { UnitPrice = 123456789.12m, VatPercent = 7.25m }, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(123456789.12m, result.Value.UnitPrice);
        Assert.Equal(7.25m, result.Value.VatPercent);
        Assert.Equal(0.10m, ProductValidator.Validate(Valid() with { UnitPrice = 0.10m }, true).Value.UnitPrice);
    }

    [Theory]
    [InlineData("100", "100.00")]
    [InlineData("100.5", "100.50")]
    [InlineData("100.500", "100.50")]
    public void Accepted_amounts_come_back_with_exactly_two_decimals(string typed, string expected)
    {
        var price = decimal.Parse(typed, System.Globalization.CultureInfo.InvariantCulture);

        var result = ProductValidator.Validate(Valid() with { UnitPrice = price, VatPercent = price / 10 }, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_missing_vat_becomes_15_on_add()
    {
        var result = ProductValidator.Validate(Valid() with { VatPercent = null }, defaultVat: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(15m, result.Value.VatPercent);
    }

    [Fact]
    public void A_vat_of_zero_is_kept_as_zero_not_replaced_by_the_default()
    {
        var result = ProductValidator.Validate(Valid() with { VatPercent = 0m }, defaultVat: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.VatPercent);
    }

    [Fact]
    public void A_missing_vat_is_an_error_on_edit()
    {
        Assert.True(ErrorsOf(Valid() with { VatPercent = null }, defaultVat: false).ContainsKey("vatPercent"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    [InlineData("100")]
    [InlineData("12.5")]
    [InlineData("0.01")]
    public void A_vat_within_range_is_accepted(string text)
    {
        var vat = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(ProductValidator.Validate(Valid() with { VatPercent = vat }, true).IsSuccess);
    }

    [Theory]
    [InlineData("100.01")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    [InlineData("15.005")]
    public void A_vat_out_of_range_or_with_a_third_decimal_is_refused(string text)
    {
        var vat = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(ErrorsOf(Valid() with { VatPercent = vat }).ContainsKey("vatPercent"));
    }

    [Fact]
    public void Several_bad_fields_are_reported_together_with_camel_case_keys()
    {
        var errors = ErrorsOf(new ProductInput(null, "AB CD", "Box", -1m, 101m));

        Assert.Equal(["name", "sku", "unit", "unitPrice", "vatPercent"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }
}
