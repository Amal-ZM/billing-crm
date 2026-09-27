using BillingCrm.Domain;
using BillingCrm.Infrastructure.Auth;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BillingCrm.Api.Tests;

public class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static JwtOptions Options(int expiryMinutes = 60) => new()
    {
        SigningKey = ApiFactory.TestSigningKey,
        Issuer = "billingcrm-api",
        Audience = "billingcrm-web",
        ExpiryMinutes = expiryMinutes,
    };

    [Fact]
    public void Issues_a_token_with_the_documented_claims_and_lifetime()
    {
        var user = StaffUser.Create("Agent@Example.Test", "hash", StaffRole.Agent);
        var service = new JwtTokenService(Options(), new FixedTimeProvider(Now));

        var issued = service.Issue(user);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(issued.AccessToken);

        Assert.Equal("billingcrm-api", token.Issuer);
        Assert.Contains("billingcrm-web", token.Audiences);
        Assert.Equal(user.Id.ToString(), token.GetClaim("sub").Value);
        Assert.Equal("Agent@Example.Test", token.GetClaim("email").Value);
        Assert.Equal("Agent", token.GetClaim("role").Value);
        Assert.False(string.IsNullOrWhiteSpace(token.GetClaim("jti").Value));
        Assert.Equal(Now.UtcDateTime, token.IssuedAt);
        Assert.Equal(Now.UtcDateTime, token.ValidFrom);
        Assert.Equal(Now.AddMinutes(60).UtcDateTime, token.ValidTo);
        Assert.Equal(Now.AddMinutes(60), issued.ExpiresAtUtc);
        Assert.Equal("HS256", token.Alg);
    }

    [Fact]
    public void The_lifetime_follows_the_configured_expiry()
    {
        var user = StaffUser.Create("admin@example.test", "hash", StaffRole.Admin);
        var service = new JwtTokenService(Options(expiryMinutes: 5), new FixedTimeProvider(Now));

        var issued = service.Issue(user);

        Assert.Equal(Now.AddMinutes(5), issued.ExpiresAtUtc);
        Assert.Equal(Now.AddMinutes(5).UtcDateTime, new JsonWebTokenHandler().ReadJsonWebToken(issued.AccessToken).ValidTo);
    }

    [Fact]
    public void Every_token_gets_its_own_id()
    {
        var user = StaffUser.Create("admin@example.test", "hash", StaffRole.Admin);
        var service = new JwtTokenService(Options(), new FixedTimeProvider(Now));

        var first = new JsonWebTokenHandler().ReadJsonWebToken(service.Issue(user).AccessToken).GetClaim("jti").Value;
        var second = new JsonWebTokenHandler().ReadJsonWebToken(service.Issue(user).AccessToken).GetClaim("jti").Value;

        Assert.NotEqual(first, second);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
