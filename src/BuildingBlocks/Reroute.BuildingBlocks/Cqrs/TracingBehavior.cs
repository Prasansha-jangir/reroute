using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Reroute.BuildingBlocks.Observability;
using Reroute.SharedKernel;

namespace Reroute.BuildingBlocks.Cqrs;

/// <summary>Every command/query becomes a span in the distributed trace plus a duration metric.</summary>
public sealed class TracingBehavior<TRequest, TResult>(ILogger<TracingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
{
    public async Task<TResult> Handle(TRequest request, Func<Task<TResult>> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        using var activity = RerouteTelemetry.ActivitySource.StartActivity($"cqrs {name}");
        activity?.SetTag("reroute.request", name);

        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            var result = await next();
            if (result is Result { IsFailure: true } failed)
            {
                outcome = failed.Error.Type.ToString();
                activity?.SetStatus(ActivityStatusCode.Error, failed.Error.Code);
                logger.LogInformation("{Request} failed with {ErrorCode}", name, failed.Error.Code);
            }

            return result;
        }
        catch (Exception ex)
        {
            outcome = "exception";
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            RerouteTelemetry.RequestDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("request", name),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
