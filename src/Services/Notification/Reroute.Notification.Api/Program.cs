using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Reroute.BuildingBlocks.Messaging;
using Reroute.BuildingBlocks.Observability;
using Reroute.BuildingBlocks.Web;
using Reroute.Contracts;
using Reroute.Notification.Api.Consumers;
using Reroute.Notification.Api.Realtime;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("notification-api", "MassTransit");
builder.AddJwtResourceServer();

// ---- Real-time push (SignalR + Redis backplane for multi-replica fan-out) ----
var redis = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSignalR().AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = RedisChannel.Literal("reroute"));

// ---- Commands / work queues: RabbitMQ locally, Azure Service Bus in the cloud — one config switch ----
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();

    if (builder.Configuration["Messaging:Transport"] == "AzureServiceBus")
    {
        x.UsingAzureServiceBus((ctx, cfg) =>
        {
            cfg.Host(builder.Configuration.GetConnectionString("ServiceBus"));
            cfg.ConfigureEndpoints(ctx);
        });
    }
    else
    {
        x.UsingRabbitMq((ctx, cfg) =>
        {
            var rabbit = builder.Configuration.GetSection("RabbitMq");
            cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
            {
                h.Username(rabbit["Username"] ?? "guest");
                h.Password(rabbit["Password"] ?? "guest");
            });
            cfg.ConfigureEndpoints(ctx);
        });
    }
});

// ---- Events: Kafka ----
builder.Services.AddKafkaProducer(builder.Configuration); // used for dead-lettering
builder.Services.AddKafkaConsumer<DisruptionDetected, DisruptionDetectedHandler>(Topics.DisruptionDetected, groupId: "notification");
builder.Services.AddKafkaConsumer<ClaimEventEnvelope, ClaimEventHandler>(Topics.ClaimsEvents, groupId: "notification");

var app = builder.Build();
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapServiceDefaults();
app.MapHub<NotificationsHub>("/hubs/notifications");

await app.RunAsync();
