using StackExchange.Redis;

namespace Reroute.Disruption.Api.Detection;

/// <summary>
/// Hot flight state in Redis. The Lua script makes "read previous + write new" ONE atomic step and drops
/// out-of-order observations, so two replicas (e.g. during a Kafka rebalance) can never both emit the same transition.
/// </summary>
public sealed class FlightStateStore(IConnectionMultiplexer redis)
{
    private const string CompareAndSetScript = """
        local ts = redis.call('HGET', KEYS[1], 'ts')
        if ts and tonumber(ts) >= tonumber(ARGV[3]) then return false end
        local prevStatus = redis.call('HGET', KEYS[1], 'status')
        local prevDelay = redis.call('HGET', KEYS[1], 'delay')
        redis.call('HSET', KEYS[1], 'status', ARGV[1], 'delay', ARGV[2], 'ts', ARGV[3])
        redis.call('EXPIRE', KEYS[1], 172800)
        if prevStatus then return prevStatus .. '|' .. prevDelay end
        return ''
        """;

    private static RedisKey FlightKey(string flightKey) => $"reroute:flight:{flightKey}";

    private static RedisKey WatchersKey(string flightKey) => $"reroute:flight:{flightKey}:watchers";

    /// <returns>(applied: false) if the observation was stale; otherwise the previous state (null if first sighting).</returns>
    public async Task<(bool Applied, FlightState? Previous)> CompareAndSetAsync(
        string flightKey, string status, int delayMinutes, DateTimeOffset observedAt)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            CompareAndSetScript,
            [FlightKey(flightKey)],
            [status, delayMinutes, observedAt.ToUnixTimeMilliseconds()]);

        if (result.IsNull)
        {
            return (false, null);
        }

        var previous = result.ToString();
        if (string.IsNullOrEmpty(previous))
        {
            return (true, null);
        }

        var parts = previous.Split('|');
        return (true, new FlightState(parts[0], int.TryParse(parts.ElementAtOrDefault(1), out var d) ? d : 0));
    }

    public async Task<FlightState?> GetAsync(string flightKey)
    {
        var values = await redis.GetDatabase().HashGetAsync(FlightKey(flightKey), ["status", "delay"]);
        return values[0].IsNull ? null : new FlightState(values[0].ToString(), (int)values[1]);
    }

    /// <summary>
    /// Local read model of "who is on this flight". In the full system it is built from itinerary.events.v1;
    /// here travellers can also register directly via POST /api/flights/watch.
    /// </summary>
    public Task WatchAsync(string flightKey, string travellerId) =>
        redis.GetDatabase().SetAddAsync(WatchersKey(flightKey), travellerId);

    public async Task<IReadOnlyList<string>> GetWatchersAsync(string flightKey) =>
        (await redis.GetDatabase().SetMembersAsync(WatchersKey(flightKey))).Select(v => v.ToString()).ToList();
}
