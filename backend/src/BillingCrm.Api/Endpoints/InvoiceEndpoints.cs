using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;

namespace BillingCrm.Api.Endpoints;

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        // Any signed-in staff member (Admin or Agent). There is no delete, un-send or reopen operation, by design: an invoice leaves
        // Draft only through the two action routes below (send, and cancel which adds the Admin-only policy).
        var group = app.MapGroup("/api/invoices").RequireAuthorization();

        // The whole invoice arrives in one request (header and every line), so a refused save changes nothing.
        group.MapPost("/", CreateAsync);
        group.MapGet("/", SearchAsync);

        // The Home dashboard's five figures (spec 011): no id, no paging, always a 200 (there is nothing to look up and nothing a
        // person can get wrong). Registered before "/{id}" for readability; ASP.NET Core's routing already prefers this literal
        // segment over "/{id}" regardless of registration order, so "summary" can never be read as an invoice id.
        group.MapGet("/summary", GetSummaryAsync);

        group.MapGet("/{id}", GetAsync);

        // Saves the whole Draft (header and every line) in one request; there is no per-line endpoint and no delete.
        group.MapPut("/{id}", UpdateAsync);

        // Draft to Sent, for any signed-in staff member. An action on the invoice, so a POST with no body (like an MVC action that
        // takes only the id); it answers with the invoice.
        group.MapPost("/{id}/send", SendAsync);

        // Draft or Sent to Cancelled: Admin only, and refused (409) once the invoice has a payment (spec 008: no refunds). The AdminOnly policy is added on top of the group's (the mixed-role pattern of the
        // products endpoints), so an Agent gets 403 before the id is even parsed or looked up.
        group.MapPost("/{id}/cancel", CancelAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);

        // Records money received against a Sent invoice, for any signed-in staff member. A payment is created under its invoice, so this is
        // a POST to a sub-resource that answers 201 with the whole updated invoice (the screen needs the new status, paid and remaining).
        // Payments are permanent by design: there is no GET, PUT or DELETE for a payment (InvoiceAccessTests pins the route list).
        group.MapPost("/{id}/payments", RecordPaymentAsync);

        // The history of one invoice (spec 010), newest first, for any signed-in user: no extra policy. It is the only operation on history, by
        // design: there is no POST, PUT, PATCH or DELETE, no read of a single entry and no query options (a query string is ignored). Entries are
        // written only by the write routes above, inside the same save as the change (InvoiceAccessTests pins the route list).
        group.MapGet("/{id}/history", GetHistoryAsync);

        return app;
    }

    private static async Task<IResult> GetHistoryAsync(string id, InvoiceService invoices, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var invoiceId))
        {
            return NotFound();
        }

        var result = await invoices.GetHistoryAsync(invoiceId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> RecordPaymentAsync(string id, PaymentInput input, InvoiceService invoices, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var invoiceId))
        {
            return NotFound();
        }

        var result = await invoices.RecordPaymentAsync(invoiceId, input, cancellationToken);
        return result.IsSuccess ? Results.Created($"/api/invoices/{invoiceId}", result.Value) : result.ToProblem();
    }

    private static async Task<IResult> CancelAsync(string id, InvoiceService invoices, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var invoiceId))
        {
            return NotFound();
        }

        var result = await invoices.CancelAsync(invoiceId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> SendAsync(string id, InvoiceService invoices, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var invoiceId))
        {
            return NotFound();
        }

        var result = await invoices.SendAsync(invoiceId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> CreateAsync(InvoiceInput input, InvoiceService invoices, CancellationToken cancellationToken)
    {
        var result = await invoices.CreateAsync(input, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/invoices/{result.Value.Id}", result.Value)
            : result.ToProblem();
    }

    // Query values arrive as text so a bad one becomes a named field error instead of a framework failure.
    private static async Task<IResult> SearchAsync(
        string? page, string? pageSize, string? status, string? overdue, string? paidMonth, InvoiceService invoices, CancellationToken cancellationToken)
    {
        var result = await invoices.SearchAsync(new InvoiceQuery(page, pageSize, status, overdue, paidMonth), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> GetSummaryAsync(InvoiceService invoices, CancellationToken cancellationToken)
    {
        var result = await invoices.GetSummaryAsync(cancellationToken);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetAsync(string id, InvoiceService invoices, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var invoiceId))
        {
            return NotFound();
        }

        var result = await invoices.GetAsync(invoiceId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(string id, InvoiceInput input, InvoiceService invoices, CancellationToken cancellationToken)
    {
        if (!TryParseId(id, out var invoiceId))
        {
            return NotFound();
        }

        var result = await invoices.UpdateAsync(invoiceId, input, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    /// <summary>A value that is not even a GUID cannot be an invoice: same answer as an unknown id.</summary>
    private static bool TryParseId(string id, out Guid invoiceId) => Guid.TryParse(id, out invoiceId);

    private static IResult NotFound() => Result.Fail(InvoiceService.NotFound).ToProblem();
}
