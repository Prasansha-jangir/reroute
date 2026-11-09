using Microsoft.AspNetCore.Http;
using Reroute.SharedKernel;

namespace Reroute.BuildingBlocks.Web;

/// <summary>Maps domain/application errors to RFC 9457 problem details consistently across services.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToProblem(this Error error) => error.Type switch
    {
        ErrorType.Validation => Results.ValidationProblem(
            error.Details?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [],
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
        _ => Results.Problem(
            title: error.Message,
            statusCode: error.Type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status422UnprocessableEntity,
            },
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
    };

    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();
}
