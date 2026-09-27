using System.Net;
using System.Net.Http.Json;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Spec 012, User Story 5, research R5: unlimited login attempts against one account or from one source were never
/// throttled. A fixed window of 10 attempts per minute, partitioned by <c>Connection.RemoteIpAddress</c> (simulated
/// per test request via <see cref="ApiFactory.TestRemoteIpHeader"/>, since the in-memory test host never has a real
/// one of its own).
/// </summary>
public class LoginRateLimitTests
{
    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? email, string? password, string sourceIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add(ApiFactory.TestRemoteIpHeader, sourceIp);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task The_11th_attempt_from_one_source_within_the_window_is_429_with_retry_after_and_the_10th_or_fewer_are_not()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var source = $"203.0.113.{Random.Shared.Next(1, 254)}";

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 11; i++)
        {
            responses.Add(await LoginAsync(client, TestUsers.AdminEmail, "wrong-password", source));
        }

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, responses[i].StatusCode); // a wrong password is refused normally, not throttled
        }

        Assert.Equal((HttpStatusCode)429, responses[10].StatusCode);
        Assert.True(responses[10].Headers.TryGetValues("Retry-After", out _));
    }

    [Fact]
    public async Task A_correct_login_within_the_window_still_succeeds()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var source = $"203.0.113.{Random.Shared.Next(1, 254)}";

        var response = await LoginAsync(client, TestUsers.AdminEmail, TestUsers.AdminPassword, source);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_different_source_is_not_throttled_by_another_sources_attempts()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var exhausted = $"203.0.113.{Random.Shared.Next(1, 254)}";
        var other = $"198.51.100.{Random.Shared.Next(1, 254)}";

        for (var i = 0; i < 10; i++)
        {
            await LoginAsync(client, TestUsers.AdminEmail, "wrong-password", exhausted);
        }

        var stillThrottled = await LoginAsync(client, TestUsers.AdminEmail, "wrong-password", exhausted);
        var fromOtherSource = await LoginAsync(client, TestUsers.AdminEmail, TestUsers.AdminPassword, other);

        Assert.Equal((HttpStatusCode)429, stillThrottled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromOtherSource.StatusCode);
    }
}
