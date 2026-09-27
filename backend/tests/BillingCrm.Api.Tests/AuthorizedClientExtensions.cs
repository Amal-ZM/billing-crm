using System.Net.Http.Headers;

namespace BillingCrm.Api.Tests;

public static class AuthorizedClientExtensions
{
    /// <summary>A client that presents a valid token for the given role ("Admin" or "Agent").</summary>
    public static HttpClient CreateAuthorizedClient(this ApiFactory factory, string role = "Agent")
    {
        var client = factory.CreateClient();
        var token = role == "Admin" ? TestTokens.Admin() : TestTokens.Agent();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
