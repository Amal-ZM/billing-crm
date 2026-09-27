using BillingCrm.Application.Common;

namespace BillingCrm.Api.Endpoints;

public static class ResultExtensions
{
    /// <summary>Maps a failed Result to a ProblemDetails response. Never includes exception text.</summary>
    public static IResult ToProblem(this Result result)
    {
        var error = result.Error
            ?? throw new InvalidOperationException("ToProblem was called on a successful result.");

        if (error.Kind == ErrorKind.Validation)
        {
            return Results.ValidationProblem(
                error.Errors?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [],
                detail: error.Detail,
                title: error.Title);
        }

        var status = error.Kind switch
        {
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(title: error.Title, detail: error.Detail, statusCode: status);
    }
}
