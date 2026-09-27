using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BillingCrm.Api.Tests;

public class LoginEndpointTests
{
    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? email, string? password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>The body minus the per-request traceId, so two failures can be compared for sameness.</summary>
    private static async Task<string> BodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }

    [Theory]
    [InlineData(TestUsers.AdminEmail, TestUsers.AdminPassword, "Admin")]
    [InlineData(TestUsers.AgentEmail, TestUsers.AgentPassword, "Agent")]
    public async Task Valid_credentials_return_200_with_role_and_a_token_that_me_accepts(string email, string password, string role)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, email, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal(role, body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("expiresAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.DoesNotContain(password, body.GetRawText());

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, meBody.GetProperty("email").GetString());
        Assert.Equal(role, meBody.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Email_is_matched_ignoring_case_and_surrounding_spaces()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, "  " + TestUsers.AdminEmail.ToUpperInvariant() + " ", TestUsers.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_401_problem_details_and_no_token()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var wrongPassword = await LoginAsync(client, TestUsers.AdminEmail, "not-the-password");
        var unknownEmail = await LoginAsync(client, "nobody@example.test", "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal("application/problem+json", wrongPassword.Content.Headers.ContentType?.MediaType);

        var wrongPasswordBody = await BodyWithoutTraceIdAsync(wrongPassword);
        var unknownEmailBody = await BodyWithoutTraceIdAsync(unknownEmail);
        Assert.Equal(wrongPasswordBody, unknownEmailBody);
        Assert.Contains("Email or password is incorrect.", wrongPasswordBody);
        Assert.DoesNotContain("accessToken", wrongPasswordBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", TestUsers.AdminPassword, "email")]
    [InlineData(null, TestUsers.AdminPassword, "email")]
    [InlineData(TestUsers.AdminEmail, "", "password")]
    [InlineData(TestUsers.AdminEmail, null, "password")]
    public async Task Missing_field_returns_400_problem_details_naming_the_field(string? email, string? password, string field)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, email, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task An_empty_or_malformed_body_is_a_400_problem_details_not_a_server_error(string body)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/login", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Oversized_credentials_are_a_400_naming_the_fields()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, new string('a', 300), new string('p', 300));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Me_without_a_token_returns_401()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
