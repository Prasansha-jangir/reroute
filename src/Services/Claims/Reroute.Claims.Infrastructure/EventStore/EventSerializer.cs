using System.Text.Json;
using System.Text.Json.Serialization;
using Reroute.Claims.Domain;
using Reroute.SharedKernel;

namespace Reroute.Claims.Infrastructure.EventStore;

/// <summary>
/// Maps event type names ↔ CLR types. The persisted name is the class name, which is therefore
/// a contract: renaming an event class would orphan history. New shapes get new names (e.g. ClaimFiledV2) + upcasters.
/// </summary>
public sealed class EventSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, // strings survive enum reordering
    };

    private readonly Dictionary<string, Type> _types = typeof(Claim).Assembly
        .GetTypes()
        .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    public static string TypeName(IDomainEvent domainEvent) => domainEvent.GetType().Name;

    public string Serialize(IDomainEvent domainEvent) =>
        JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);

    public IDomainEvent Deserialize(string eventType, string data) =>
        _types.TryGetValue(eventType, out var type)
            ? (IDomainEvent)JsonSerializer.Deserialize(data, type, Options)!
            : throw new InvalidOperationException($"Unknown event type '{eventType}'. Missing upcaster?");
}
