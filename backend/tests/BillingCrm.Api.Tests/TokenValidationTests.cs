using System.Net;
using System.Net.Http.Headers;

namespace BillingCrm.Api.Tests;

public class TokenValidationTests
{
    private static async Task<HttpResponseMessage> GetMeAsync(string? token)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_valid_token_is_accepted()
    {
        var response = await GetMeAsync(TestTokens.Agent());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_refused_with_401_problem_details()
    {
        var response = await GetMeAsync(TestTokens.Expired());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_token_that_expired_a_minute_ago_is_refused_so_no_clock_skew_grace_applies()
    {
        // The framework default allows 5 minutes of skew, which would still accept this token.
        var token = TestTokens.Create("late@crm.test", "Admin", DateTime.UtcNow.AddMinutes(-2), TimeSpan.FromMinutes(1));

        var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_refused()
    {
        var response = await GetMeAsync(TestTokens.Tampered());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_a_different_key_is_refused()
    {
        var token = TestTokens.Create("x@crm.test", "Admin", signingKey: "a-completely-different-test-only-key-9876543210");

        var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_a_different_issuer_is_refused()
    {
        var response = await GetMeAsync(TestTokens.WithIssuer("someone-else"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Garbage_in_the_authorization_header_is_refused()
    {
        var response = await GetMeAsync("not-a-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void A_missing_signing_key_stops_startup_and_names_the_setting()
    {
        using var factory = new ApiFactory(signingKey: null);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", exception.Message);
    }

    [Fact]
    public void A_low_entropy_signing_key_stops_startup()
    {
        using var factory = new ApiFactory(signingKey: new string('x', 64));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", exception.Message);
        Assert.Contains("not random enough", exception.Message);
    }

    [Fact]
    public void A_too_short_signing_key_stops_startup_without_echoing_the_value()
    {
        const string shortKey = "shortkey-value-xyz";
        using var factory = new ApiFactory(signingKey: shortKey);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", exception.Message);
        Assert.Contains("32 bytes", exception.Message);
        Assert.DoesNotContain(shortKey, exception.Message);
    }
}
