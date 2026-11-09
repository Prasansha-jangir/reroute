using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Reroute.BuildingBlocks.Observability;

/// <summary>Single ActivitySource/Meter for custom spans and business metrics across all services.</summary>
public static class RerouteTelemetry
{
    public const string Name = "Reroute";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static readonly Meter Meter = new(Name);

    public static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("reroute.cqrs.request.duration", unit: "ms", description: "Command/query handler duration");

    public static readonly Histogram<double> OutboxPublishLag =
        Meter.CreateHistogram<double>("reroute.outbox.publish_lag", unit: "ms", description: "Time from commit to broker ack");

    public static readonly Counter<long> MessagesConsumed =
        Meter.CreateCounter<long>("reroute.messages.consumed", description: "Messages consumed, by topic and outcome");
}
