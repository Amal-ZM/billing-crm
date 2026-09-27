using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingCrm.Api.Tests;

public class AdminEndpointTests
{
    private static async Task<HttpResponseMessage> GetAdminStatusAsync(ApiFactory factory, string? token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/status");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Admin_token_gets_200_and_the_placeholder_body()
    {
        await using var factory = new ApiFactory();

        var response = await GetAdminStatusAsync(factory, TestTokens.Admin());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin", body.GetProperty("area").GetString());
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Agent_token_is_refused_with_403_problem_details()
    {
        await using var factory = new ApiFactory();

        var response = await GetAdminStatusAsync(factory, TestTokens.Agent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, body.GetProperty("status").GetInt32());
        Assert.Equal("Forbidden", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task No_token_is_refused_with_401_problem_details()
    {
        await using var factory = new ApiFactory();

        var response = await GetAdminStatusAsync(factory, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_token_from_a_real_agent_sign_in_is_refused_by_the_admin_endpoint()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = TestUsers.AgentEmail, password = TestUsers.AgentPassword });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var response = await GetAdminStatusAsync(factory, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_a_real_admin_sign_in_is_accepted_by_the_admin_endpoint()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = TestUsers.AdminEmail, password = TestUsers.AdminPassword });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var response = await GetAdminStatusAsync(factory, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_is_still_open_without_a_token()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Every_mapped_endpoint_declares_its_access_explicitly()
    {
        using var factory = new ApiFactory();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        Assert.NotEmpty(endpoints);
        var undeclared = endpoints
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(endpoint => endpoint.DisplayName ?? endpoint.RoutePattern.RawText)
            .ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void The_anonymous_endpoints_are_exactly_health_and_login()
    {
        using var factory = new ApiFactory();
        var anonymous = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Order()
            .ToList();

        Assert.Equal(new[] { "/api/auth/login", "/health" }, anonymous);
    }

    [Fact]
    public async Task An_unannotated_endpoint_would_be_closed_because_the_fallback_policy_requires_a_signed_in_user()
    {
        using var factory = new ApiFactory();
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }
}
