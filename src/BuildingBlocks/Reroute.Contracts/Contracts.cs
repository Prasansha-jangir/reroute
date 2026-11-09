namespace Reroute.Contracts;

/// <summary>Raw observation from a flight-status provider. Topic: flight-status.raw.v1, key: FlightKey.</summary>
public sealed record FlightStatusUpdate(
    string FlightKey,
    string FlightNumber,
    DateOnly FlightDate,
    string Origin,
    string Destination,
    string Status,
    int DelayMinutes,
    string? Reason,
    DateTimeOffset ObservedAt);

public static class FlightStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Delayed = "Delayed";
    public const string Cancelled = "Cancelled";
    public const string Diverted = "Diverted";
    public const string Landed = "Landed";
}

/// <summary>A meaningful disruption for travellers. Topic: disruption.detected.v1, key: FlightKey.</summary>
public sealed record DisruptionDetected(
    Guid DisruptionId,
    string FlightKey,
    string FlightNumber,
    DateOnly FlightDate,
    string Origin,
    string Destination,
    string Type,
    int DelayMinutes,
    string? Reason,
    bool LikelyExtraordinary,
    IReadOnlyList<string> AffectedTravellerIds,
    DateTimeOffset DetectedAt);

/// <summary>Envelope for claims domain events published from the Claims outbox. Topic: claims.events.v1, key: ClaimId.</summary>
public sealed record ClaimEventEnvelope(Guid ClaimId, string TravellerId, string EventType, int Version, string Data, DateTimeOffset OccurredAt);

/// <summary>RabbitMQ command (point-to-point, retried, dead-lettered): deliver one notification to one recipient.</summary>
public sealed record SendNotification(
    Guid NotificationId,
    string RecipientId,
    string Channel,
    string Category,
    string Title,
    string Body,
    string? Link);

public static class NotificationChannels
{
    public const string InApp = "in-app";
    public const string Email = "email";
}
