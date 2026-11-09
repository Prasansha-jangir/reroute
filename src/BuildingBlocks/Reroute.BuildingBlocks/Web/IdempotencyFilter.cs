using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Reroute.BuildingBlocks.Web;

/// <summary>
/// Idempotency-Key support for POST endpoints (Stripe/Adyen style).
/// First request: SET key "processing" NX → run handler → store status+body for 24 h.
/// Retry with same key: replay stored response. Concurrent duplicate while processing: 409.
/// No Redis registered (e.g. unit tests) → filter is a no-op.
/// </summary>
public sealed class IdempotencyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const string Processing = "__processing__";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var redis = http.RequestServices.GetService<IConnectionMultiplexer>();
        if (redis is null || !http.Request.Headers.TryGetValue(HeaderName, out var keyValues) ||
            !Guid.TryParse(keyValues.ToString(), out var idempotencyKey))
        {
            return await next(context);
        }

        // Keys are scoped per user so one user can never replay another user's response.
        var user = http.User.FindFirst("sub")?.Value ?? "anonymous";
        var key = new RedisKey($"reroute:idem:{http.Request.Path}:{user}:{idempotencyKey}");
        var db = redis.GetDatabase();

        if (!await db.StringSetAsync(key, Processing, Ttl, When.NotExists))
        {
            var stored = await db.StringGetAsync(key);
            if (stored == Processing)
            {
                return Results.Problem(title: "A request with this Idempotency-Key is still being processed.", statusCode: StatusCodes.Status409Conflict);
            }

            var cached = JsonSerializer.Deserialize<StoredResponse>(stored.ToString(), Json)!;
            http.Response.Headers["Idempotent-Replayed"] = "true";
            return Results.Json(cached.Body, statusCode: cached.StatusCode);
        }

        try
        {
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: < 500 } statusResult && result is IValueHttpResult valueResult)
            {
                var body = JsonSerializer.SerializeToElement(valueResult.Value, Json);
                await db.StringSetAsync(key, JsonSerializer.Serialize(new StoredResponse(statusResult.StatusCode ?? 200, body), Json), Ttl);
            }
            else
            {
                await db.KeyDeleteAsync(key); // don't pin server errors; allow retry
            }

            return result;
        }
        catch
        {
            await db.KeyDeleteAsync(key);
            throw;
        }
    }

    private sealed record StoredResponse(int StatusCode, JsonElement Body);
}

public static class IdempotencyEndpointExtensions
{
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<IdempotencyFilter>();
}
