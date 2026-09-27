using BillingCrm.Infrastructure.Auth;

namespace BillingCrm.Api.Tests;

public class JwtOptionsTests
{
    private static JwtOptions Valid() => new()
    {
        SigningKey = ApiFactory.TestSigningKey,
        Issuer = "billingcrm-api",
        Audience = "billingcrm-web",
        ExpiryMinutes = 60,
    };

    [Fact]
    public void Valid_options_pass()
    {
        Valid().Validate();
    }

    [Fact]
    public void A_repeated_character_key_is_rejected_as_not_random_without_echoing_it()
    {
        var options = Valid();
        options.SigningKey = new string('a', 64);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Jwt:SigningKey", exception.Message);
        Assert.Contains("not random enough", exception.Message);
        Assert.DoesNotContain(new string('a', 10), exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_issuer_is_rejected(string? issuer)
    {
        var options = Valid();
        options.Issuer = issuer;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Jwt:Issuer", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_audience_is_rejected(string? audience)
    {
        var options = Valid();
        options.Audience = audience;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Jwt:Audience", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public void An_out_of_range_lifetime_is_rejected(int minutes)
    {
        var options = Valid();
        options.ExpiryMinutes = minutes;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Jwt:ExpiryMinutes", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public void The_lifetime_limits_are_inclusive(int minutes)
    {
        var options = Valid();
        options.ExpiryMinutes = minutes;

        options.Validate();
    }
}
