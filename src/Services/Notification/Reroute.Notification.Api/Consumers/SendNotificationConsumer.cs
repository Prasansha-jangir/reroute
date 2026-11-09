using System.Net.Mail;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Reroute.Contracts;
using Reroute.Notification.Api.Realtime;

namespace Reroute.Notification.Api.Consumers;

/// <summary>
/// RabbitMQ (or Service Bus) work-queue consumer: delivers ONE notification on ONE channel.
/// Failures are retried by MassTransit, then moved to the "send-notification_error" queue for inspection/replay.
/// </summary>
public sealed class SendNotificationConsumer(
    IHubContext<NotificationsHub> hub,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SendNotificationConsumer> logger) : IConsumer<SendNotification>
{
    public async Task Consume(ConsumeContext<SendNotification> context)
    {
        var n = context.Message;
        switch (n.Channel)
        {
            case NotificationChannels.InApp:
                await hub.Clients.User(n.RecipientId).SendAsync(
                    "notification",
                    new NotificationMessage(n.NotificationId, n.Category, n.Title, n.Body, n.Link, timeProvider.GetUtcNow()),
                    context.CancellationToken);
                break;

            case NotificationChannels.Email:
                // Locally: Mailpit captures everything (http://localhost:8025). In Azure: Communication Services Email.
                using (var smtp = new SmtpClient(configuration["Smtp:Host"] ?? "localhost", configuration.GetValue("Smtp:Port", 1025)))
                using (var mail = new MailMessage("alerts@reroute.test", $"{n.RecipientId}@reroute.test", n.Title, n.Body))
                {
                    await smtp.SendMailAsync(mail, context.CancellationToken);
                }

                break;

            default:
                throw new InvalidOperationException($"Unknown channel '{n.Channel}'."); // → retries exhausted → _error queue
        }

        logger.LogInformation("Delivered {Category} notification {Id} via {Channel}", n.Category, n.NotificationId, n.Channel);
    }
}

public sealed class SendNotificationConsumerDefinition : ConsumerDefinition<SendNotificationConsumer>
{
    public SendNotificationConsumerDefinition()
    {
        EndpointName = "send-notification";
        ConcurrentMessageLimit = 8; // back-pressure: at most 8 in flight per replica
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SendNotificationConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        endpointConfigurator.PrefetchCount = 16;
    }
}
