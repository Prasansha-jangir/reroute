using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Reroute.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaProducer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.AddSingleton<IMessageProducer, KafkaMessageProducer>();
        return services;
    }

    /// <summary>Registers a hosted Kafka consumer that dispatches <typeparamref name="TMessage"/> to <typeparamref name="THandler"/>.</summary>
    public static IServiceCollection AddKafkaConsumer<TMessage, THandler>(
        this IServiceCollection services, string topic, string groupId)
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new KafkaConsumerService<TMessage, THandler>.Settings(topic, groupId));
        services.AddHostedService<KafkaConsumerService<TMessage, THandler>>();
        return services;
    }

    public static IServiceCollection AddOutboxPublisher(this IServiceCollection services, IConfiguration configuration, string schema)
    {
        services.Configure<OutboxOptions>(o =>
        {
            o.Schema = schema;
            configuration.GetSection(OutboxOptions.SectionName).Bind(o);
        });
        services.AddHostedService<OutboxPublisher>();
        return services;
    }
}
