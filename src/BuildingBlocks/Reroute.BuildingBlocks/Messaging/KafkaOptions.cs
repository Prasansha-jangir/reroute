using Confluent.Kafka;

namespace Reroute.BuildingBlocks.Messaging;

/// <summary>
/// Kafka connection settings. Locally: plaintext broker. In Azure: Event Hubs Kafka endpoint
/// (SecurityProtocol=SaslSsl, SaslMechanism=Plain, SaslUsername="$ConnectionString") — same client code.
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    public SecurityProtocol? SecurityProtocol { get; set; }

    public SaslMechanism? SaslMechanism { get; set; }

    public string? SaslUsername { get; set; }

    public string? SaslPassword { get; set; }

    internal T Apply<T>(T config)
        where T : ClientConfig
    {
        config.BootstrapServers = BootstrapServers;
        config.SecurityProtocol = SecurityProtocol;
        config.SaslMechanism = SaslMechanism;
        config.SaslUsername = SaslUsername;
        config.SaslPassword = SaslPassword;
        return config;
    }
}

public static class MessageHeaders
{
    public const string MessageId = "message-id";
    public const string MessageType = "message-type";
    public const string TraceParent = "traceparent";
    public const string Error = "x-error";
    public const string OriginalTopic = "x-original-topic";
}

public static class Topics
{
    public const string FlightStatusRaw = "flight-status.raw.v1";
    public const string DisruptionDetected = "disruption.detected.v1";
    public const string ItineraryEvents = "itinerary.events.v1";
    public const string ClaimsEvents = "claims.events.v1";

    public static string DeadLetter(string topic) => topic + ".dlq";
}
