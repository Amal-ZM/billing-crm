using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BillingCrm.Api.Tests;

public class CorsTests
{
    private const string AllowOriginHeader = "Access-Control-Allow-Origin";

    private static async Task<HttpResponseMessage> GetHealthFromOriginAsync(ApiFactory factory, string origin)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Allowed_origin_gets_allow_origin_header_echoed()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy);

        var response = await GetHealthFromOriginAsync(factory, "http://localhost:4200");

        Assert.True(response.Headers.TryGetValues(AllowOriginHeader, out var values));
        Assert.Equal("http://localhost:4200", Assert.Single(values));
    }

    [Fact]
    public async Task Other_origin_gets_no_allow_origin_header()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy);

        var response = await GetHealthFromOriginAsync(factory, "http://evil.example");

        Assert.False(response.Headers.Contains(AllowOriginHeader));
    }

    [Fact]
    public async Task Allowed_origins_come_from_configuration()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy, corsOriginOverride: "http://configured.example");

        var configured = await GetHealthFromOriginAsync(factory, "http://configured.example");
        var formerDefault = await GetHealthFromOriginAsync(factory, "http://localhost:4200");

        Assert.True(configured.Headers.Contains(AllowOriginHeader));
        Assert.False(formerDefault.Headers.Contains(AllowOriginHeader));
    }

    [Fact]
    public async Task Preflight_from_allowed_origin_is_answered_with_allow_origin_header()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues(AllowOriginHeader, out var values));
        Assert.Equal("http://localhost:4200", Assert.Single(values));
    }

    [Fact]
    public async Task Preflight_asking_for_an_unexpected_header_is_not_approved()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "x-not-allowed");

        var response = await client.SendAsync(request);

        // The browser refuses the real request unless the header appears in Access-Control-Allow-Headers.
        var allowedHeaders = response.Headers.TryGetValues("Access-Control-Allow-Headers", out var values)
            ? string.Join(",", values)
            : string.Empty;
        Assert.DoesNotContain("x-not-allowed", allowedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preflight_asking_for_the_authorization_header_is_approved()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains(AllowOriginHeader));
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out var values));
        Assert.Contains("authorization", string.Join(",", values), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wildcard_origin_in_configuration_fails_startup()
    {
        using var factory = new ApiFactory(HealthStatus.Healthy, corsOriginOverride: "*");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", exception.Message);
    }
}
