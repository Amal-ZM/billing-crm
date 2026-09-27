using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BillingCrm.Api.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Get_health_returns_200_and_healthy_body_when_database_is_healthy_without_credentials()
    {
        await using var factory = new ApiFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Get_health_returns_503_naming_the_database_and_leaks_no_connection_details_when_database_is_unhealthy()
    {
        await using var factory = new ApiFactory(HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Placeholder", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", raw, StringComparison.OrdinalIgnoreCase);

        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", body.GetProperty("checks").GetProperty("database").GetString());
    }
}
