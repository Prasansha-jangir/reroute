using System.Diagnostics.Metrics;
using System.Text.Json;
using Reroute.BuildingBlocks.Messaging;
using Reroute.BuildingBlocks.Observability;
using Reroute.Contracts;

namespace Reroute.Disruption.Api.Detection;

/// <summary>
/// flight-status.raw.v1 → (Redis compare-and-set) → disruption.detected.v1.
/// Publishing directly (no outbox) is safe here: this service has no database of its own, the Redis CAS makes
/// detection replay-safe, and the producer is idempotent. Re-processing a message can at worst re-emit an
/// identical event, which consumers dedupe by DisruptionId (derived deterministically below).
/// </summary>
public sealed class FlightStatusHandler(
    FlightStateStore store,
    IMessageProducer producer,
    TimeProvider timeProvider,
    ILogger<FlightStatusHandler> logger) : IMessageHandler<FlightStatusUpdate>
{
    private static readonly Counter<long> Detected =
        RerouteTelemetry.Meter.CreateCounter<long>("reroute.disruptions.detected");

    private static readonly Histogram<double> DetectionLatency =
        RerouteTelemetry.Meter.CreateHistogram<double>("reroute.disruption.detection_latency", unit: "ms");

    public async Task HandleAsync(FlightStatusUpdate message, MessageContext context, CancellationToken cancellationToken)
    {
        var (applied, previous) = await store.CompareAndSetAsync(message.FlightKey, message.Status, message.DelayMinutes, message.ObservedAt);
        if (!applied)
        {
            logger.LogDebug("Dropped stale observation for {FlightKey}", message.FlightKey);
            return;
        }

        var type = DisruptionDetector.Detect(previous, message);
        if (type is null)
        {
            return;
        }

        var disruption = new DisruptionDetected(
            DeterministicId(message.FlightKey, type, message.DelayMinutes),
            message.FlightKey,
            message.FlightNumber,
            message.FlightDate,
            message.Origin,
            message.Destination,
            type,
            message.DelayMinutes,
            message.Reason,
            DisruptionDetector.LikelyExtraordinary(message.Reason),
            await store.GetWatchersAsync(message.FlightKey),
            timeProvider.GetUtcNow());

        await producer.ProduceAsync(
            new OutgoingMessage(
                Topics.DisruptionDetected,
                message.FlightKey,
                nameof(DisruptionDetected),
                JsonSerializer.Serialize(disruption, JsonSerializerOptions.Web),
                disruption.DisruptionId),
            cancellationToken);

        Detected.Add(1, new KeyValuePair<string, object?>("type", type));
        DetectionLatency.Record((timeProvider.GetUtcNow() - message.ObservedAt).TotalMilliseconds);
        logger.LogInformation("Detected {Type} on {FlightKey} ({Delay} min) affecting {Count} travellers",
            type, message.FlightKey, message.DelayMinutes, disruption.AffectedTravellerIds.Count);
    }

    /// <summary>Same flight + type + threshold → same ID → downstream inboxes dedupe replays.</summary>
    private static Guid DeterministicId(string flightKey, string type, int delayMinutes)
    {
        var bucket = type == "Delay" ? (delayMinutes >= DisruptionDetector.CompensationDelayMinutes ? "3h" : "1h") : "-";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{flightKey}|{type}|{bucket}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
