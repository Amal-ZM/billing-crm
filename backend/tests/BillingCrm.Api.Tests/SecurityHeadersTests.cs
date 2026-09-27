using System.Net.Http.Headers;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Spec 012, User Story 5, research R3: nothing on this site should ever be frame-able by another origin, and an
/// authenticated screen must never be restored from a shared machine's back/forward cache. A blanket rule on every
/// response (<c>X-Frame-Options</c>) plus every <c>/api</c> response (<c>Cache-Control: no-store</c>).
/// </summary>
public class SecurityHeadersTests
{
    [Fact]
    public async Task Every_response_including_an_anonymous_one_carries_X_Frame_Options_Deny()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");

        Assert.True(health.Headers.TryGetValues("X-Frame-Options", out var values));
        Assert.Equal("DENY", Assert.Single(values!));
    }

    [Fact]
    public async Task An_authenticated_api_response_also_carries_Cache_Control_no_store()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync("/api/invoices");

        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var frameValues));
        Assert.Equal("DENY", Assert.Single(frameValues!));
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
}
