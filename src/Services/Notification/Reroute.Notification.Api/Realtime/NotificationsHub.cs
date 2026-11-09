using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Reroute.Notification.Api.Realtime;

/// <summary>
/// WebSocket hub. Clients only listen; the server pushes to Clients.User(sub).
/// With the Redis backplane, a message sent from any replica reaches the replica holding the user's connection.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub;

public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}

/// <summary>Payload shape the React RealtimeProvider expects.</summary>
public sealed record NotificationMessage(Guid Id, string Category, string Title, string Body, string? Link, DateTimeOffset SentAt);
