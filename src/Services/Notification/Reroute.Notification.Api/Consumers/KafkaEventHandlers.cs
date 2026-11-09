using System.Text.Json;
using MassTransit;
using Reroute.BuildingBlocks.Messaging;
using MessageContext = Reroute.BuildingBlocks.Messaging.MessageContext;
using Reroute.Contracts;

namespace Reroute.Notification.Api.Consumers;

/// <summary>
/// Bridge from the event log (Kafka: "this happened") to work queues (RabbitMQ: "do this for this person").
/// One fact fans out into N per-recipient commands, each retried and dead-lettered independently.
/// </summary>
public sealed class DisruptionDetectedHandler(IPublishEndpoint publisher) : IMessageHandler<DisruptionDetected>
{
    public async Task HandleAsync(DisruptionDetected message, MessageContext context, CancellationToken cancellationToken)
    {
        var title = message.Type switch
        {
            "Cancellation" => $"{message.FlightNumber} has been cancelled",
            "Diversion" => $"{message.FlightNumber} has been diverted",
            _ => $"{message.FlightNumber} is delayed by {message.DelayMinutes / 60}h{message.DelayMinutes % 60:00}",
        };
        var body = message.LikelyExtraordinary
            ? "We're tracking alternatives for you. The reported cause may limit compensation — ask the copilot for details."
            : "You may be entitled to up to €600 under EU261. Tap to see your options.";

        foreach (var travellerId in message.AffectedTravellerIds)
        {
            foreach (var channel in new[] { NotificationChannels.InApp, NotificationChannels.Email })
            {
                await publisher.Publish(
                    new SendNotification(
                        DeterministicId(message.DisruptionId, travellerId, channel),
                        travellerId,
                        channel,
                        "disruption",
                        title,
                        body,
                        $"/disruptions/{message.FlightKey}"),
                    cancellationToken);
            }
        }
    }

    private static Guid DeterministicId(Guid source, string recipient, string channel) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source}|{recipient}|{channel}")).AsSpan(0, 16));
}

public sealed class ClaimEventHandler(IPublishEndpoint publisher) : IMessageHandler<ClaimEventEnvelope>
{
    public async Task HandleAsync(ClaimEventEnvelope message, MessageContext context, CancellationToken cancellationToken)
    {
        var (title, body) = message.EventType switch
        {
            "EligibilityAssessed" => ("Your claim was assessed", ReadReason(message.Data)),
            "AirlineAcceptedClaim" => ("Good news: the airline accepted your claim", "Your compensation is on its way."),
            "AirlineRejectedClaim" => ("The airline rejected your claim", "The copilot can help you escalate to the national enforcement body."),
            "CompensationPaid" => ("Compensation paid", "The money has been sent to your account."),
            _ => (null, null),
        };

        if (title is null)
        {
            return;
        }

        await publisher.Publish(
            new SendNotification(context.MessageId, message.TravellerId, NotificationChannels.InApp, "claim", title, body!, $"/claims/{message.ClaimId}"),
            cancellationToken);
    }

    private static string ReadReason(string data) =>
        JsonDocument.Parse(data).RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty;
}
