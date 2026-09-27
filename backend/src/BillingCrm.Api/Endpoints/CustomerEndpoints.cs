using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;

namespace BillingCrm.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        // Any signed-in staff member (Admin or Agent); there is no delete operation, by design.
        var group = app.MapGroup("/api/customers").RequireAuthorization();

        group.MapPost("/", CreateAsync);
        group.MapGet("/", SearchAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPut("/{id}", UpdateAsync);

        // Retire or restore. This, not a delete, is how a customer leaves active use.
        group.MapPut("/{id}/active", SetActiveAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(CustomerInput input, CustomerService customers, CancellationToken cancellationToken)
    {
        var result = await customers.CreateAsync(input, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/customers/{result.Value.Id}", result.Value)
            : result.ToProblem();
    }

    // Query values arrive as text so a bad one becomes a named field error instead of a framework failure.
    private static async Task<IResult> SearchAsync(
        string? q,
        string? type,
        string? active,
        string? page,
        string? pageSize,
        CustomerService customers,
        CancellationToken cancellationToken)
    {
        var result = await customers.SearchAsync(new CustomerQuery(q, type, active, page, pageSize), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> GetAsync(string id, CustomerService customers, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var customerId))
        {
            return NotFound();
        }

        var result = await customers.GetAsync(customerId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(string id, CustomerInput input, CustomerService customers, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var customerId))
        {
            return NotFound();
        }

        var result = await customers.UpdateAsync(customerId, input, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> SetActiveAsync(string id, SetActiveRequest request, CustomerService customers, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var customerId))
        {
            return NotFound();
        }

        var result = await customers.SetActiveAsync(customerId, request, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    /// <summary>A value that is not even a GUID cannot be a customer: same answer as an unknown id.</summary>
    private static bool TryParseId(string id, out Guid customerId) => Guid.TryParse(id, out customerId);

    private static IResult NotFound() => Result.Fail(CustomerService.NotFound).ToProblem();
}
