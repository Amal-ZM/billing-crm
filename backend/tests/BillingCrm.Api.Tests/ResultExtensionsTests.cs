using System.Text.Json;
using BillingCrm.Api.Endpoints;
using BillingCrm.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BillingCrm.Api.Tests;

public class ResultExtensionsTests
{
    private static async Task<(int Status, string Body)> ExecuteAsync(Result result)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await result.ToProblem().ExecuteAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData(ErrorKind.Unauthorized, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    public async Task Each_kind_maps_to_its_status_with_the_error_text_and_nothing_else(ErrorKind kind, int expectedStatus)
    {
        var error = new Error(kind, "some_code", "Some title", "Some detail");

        var (status, body) = await ExecuteAsync(Result.Fail(error));

        Assert.Equal(expectedStatus, status);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("Some title", json.GetProperty("title").GetString());
        Assert.Equal("Some detail", json.GetProperty("detail").GetString());
        Assert.Equal(expectedStatus, json.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Validation_maps_to_400_with_the_errors_dictionary()
    {
        var error = Error.Validation(new Dictionary<string, string[]> { ["email"] = ["Email is required."] });

        var (status, body) = await ExecuteAsync(Result.Fail(error));

        Assert.Equal(400, status);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("Email is required.", json.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Fact]
    public void Mapping_a_successful_result_is_a_programming_error()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Success().ToProblem());
    }
}
