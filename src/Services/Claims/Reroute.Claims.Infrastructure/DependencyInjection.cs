using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reroute.BuildingBlocks.Messaging;
using Reroute.BuildingBlocks.Persistence;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Infrastructure.EventStore;
using Reroute.Claims.Infrastructure.Messaging;
using Reroute.Claims.Infrastructure.Persistence;
using Reroute.Claims.Infrastructure.ReadModel;
using Reroute.Claims.Infrastructure.Reference;
using Reroute.Contracts;
using StackExchange.Redis;

namespace Reroute.Claims.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var sqlConnectionString = configuration.GetConnectionString("ClaimsDb")
            ?? throw new InvalidOperationException("ConnectionStrings:ClaimsDb is required.");
        var redisConnectionString = configuration.GetConnectionString("Redis");

        services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(sqlConnectionString));
        services.AddSingleton<EventSerializer>();
        services.AddSingleton<SqlEventStore>();
        services.AddSingleton<IAirportDirectory, InMemoryAirportDirectory>();
        services.AddScoped<IClaimRepository, EventSourcedClaimRepository>();
        services.AddScoped<SqlClaimReadStore>();
        services.AddScoped<IClaimReadStore, CachedClaimReadStore>();
        services.AddScoped<IInbox>(sp => new SqlInbox(sp.GetRequiredService<ISqlConnectionFactory>(), EventSourcedClaimRepository.Schema));

        // HybridCache: L1 in-memory always; L2 = Redis when configured (IDistributedCache is picked up automatically).
        services.AddHybridCache();
        var health = services.AddHealthChecks().AddSqlServer(sqlConnectionString, name: "sql");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redisConnectionString;
                o.InstanceName = "reroute:";
            });
            health.AddRedis(redisConnectionString, name: "redis");
        }

        // Messaging: outbox → Kafka; consume disruptions.
        services.AddKafkaProducer(configuration);
        services.AddOutboxPublisher(configuration, EventSourcedClaimRepository.Schema);
        if (configuration.GetValue("Kafka:ConsumersEnabled", true))
        {
            services.AddKafkaConsumer<DisruptionDetected, DisruptionDetectedHandler>(
                BuildingBlocks.Messaging.Topics.DisruptionDetected, groupId: "claims");
        }

        return services;
    }
}
