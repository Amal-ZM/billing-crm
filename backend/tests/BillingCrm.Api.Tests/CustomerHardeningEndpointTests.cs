using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingCrm.Api.Tests;

/// <summary>Follow-ups from the spec 003 reviews.</summary>
public class CustomerHardeningEndpointTests
{
    private static object Body(string name = "Acme Trading") =>
        new { name, type = "Company", email = $"{Guid.NewGuid():N}@acme.example", phone = "0500000000", address = "Somewhere" };

    [Theory]
    [InlineData("?page=2147483647&pageSize=100")]
    [InlineData("?page=21474838&pageSize=100")]
    [InlineData("?page=2147483647&pageSize=1")]
    public async Task A_huge_page_number_is_a_200_with_no_items_and_the_real_total_never_a_server_error(string query)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();
        await client.PostAsJsonAsync("/api/customers", Body());

        var response = await client.GetAsync("/api/customers" + query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_page_number_too_big_for_an_int_is_a_400_naming_page()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.GetAsync("/api/customers?page=99999999999999999999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("page", out _));
    }

    [Fact]
    public async Task Search_text_over_100_characters_is_a_400_naming_q_and_100_is_fine()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var tooLong = await client.GetAsync("/api/customers?q=" + new string('a', 101));
        var ok = await client.GetAsync("/api/customers?q=" + new string('a', 100));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.True((await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("q", out _));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task An_over_length_name_is_a_400_whose_message_states_the_maximum_and_nothing_is_saved()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/customers", Body(name: new string('a', 201)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("name")[0].GetString();
        Assert.Equal("Name must be at most 200 characters.", message);
        Assert.Empty(factory.CustomerStore.All);
    }

    [Fact]
    public void No_customer_route_accepts_DELETE_and_only_the_documented_operations_exist()
    {
        using var factory = new ApiFactory();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/customers", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => $"{method} {endpoint.RoutePattern.RawText!.TrimEnd('/')}"))
            .Order()
            .ToList();

        Assert.DoesNotContain(routes, route => route.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "GET /api/customers",
                "GET /api/customers/{id}",
                "POST /api/customers",
                "PUT /api/customers/{id}",
                "PUT /api/customers/{id}/active",
            },
            routes);
    }

    [Fact]
    public async Task Cross_origin_preflight_for_delete_is_not_approved()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/customers/" + Guid.NewGuid());
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "DELETE");

        var response = await client.SendAsync(request);

        var allowed = response.Headers.TryGetValues("Access-Control-Allow-Methods", out var values) ? string.Join(",", values) : string.Empty;
        Assert.DoesNotContain("DELETE", allowed, StringComparison.OrdinalIgnoreCase);
    }
}
