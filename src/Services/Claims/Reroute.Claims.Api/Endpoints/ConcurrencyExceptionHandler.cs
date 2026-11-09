using Microsoft.AspNetCore.Diagnostics;
using Reroute.Claims.Application.Abstractions;

namespace Reroute.Claims.Api.Endpoints;

/// <summary>Concurrent writers to the same stream → 409 Conflict; the client reloads and retries.</summary>
internal sealed class ConcurrencyExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConcurrencyException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = "The claim was modified by another request. Reload and try again.",
                Status = StatusCodes.Status409Conflict,
            },
        });
    }
}
