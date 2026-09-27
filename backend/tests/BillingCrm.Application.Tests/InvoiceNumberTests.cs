using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class InvoiceNumberTests
{
    [Theory]
    [InlineData(1, "INV-000001")]
    [InlineData(9, "INV-000009")]
    [InlineData(123, "INV-000123")]
    [InlineData(12345, "INV-012345")]
    [InlineData(999999, "INV-999999")]
    public void Format_pads_the_number_to_six_digits(int number, string expected)
    {
        Assert.Equal(expected, InvoiceNumber.Format(number));
    }

    [Theory]
    [InlineData("INV-000001", 1)]
    [InlineData("INV-000123", 123)]
    [InlineData("INV-999999", 999999)]
    public void TryParse_accepts_exactly_the_formatted_shape(string text, int expected)
    {
        Assert.True(InvoiceNumber.TryParse(text, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("INV-1")]
    [InlineData("INV-00001")]
    [InlineData("INV-0000001")]
    [InlineData("inv-000001")]
    [InlineData("Inv-000001")]
    [InlineData(" INV-000001")]
    [InlineData("INV-000001 ")]
    [InlineData("INV000001")]
    [InlineData("INV-00000A")]
    [InlineData("INV-+00001")]
    [InlineData("INV--00001")]
    [InlineData("INV-٠٠٠٠٠١")]
    [InlineData("123")]
    public void TryParse_refuses_anything_else(string? text)
    {
        Assert.False(InvoiceNumber.TryParse(text, out var number));
        Assert.Equal(0, number);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(999999)]
    public void Format_and_TryParse_are_inverses(int value)
    {
        Assert.True(InvoiceNumber.TryParse(InvoiceNumber.Format(value), out var parsed));
        Assert.Equal(value, parsed);
    }

    [Fact]
    public void The_largest_number_is_the_largest_six_digit_number()
    {
        Assert.Equal(999_999, InvoiceNumber.Max);
        Assert.Equal("INV-999999", InvoiceNumber.Format(InvoiceNumber.Max));
    }
}
