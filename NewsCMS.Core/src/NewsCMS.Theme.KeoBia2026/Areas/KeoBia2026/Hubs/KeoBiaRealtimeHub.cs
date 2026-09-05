using Microsoft.AspNetCore.SignalR;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Hubs;

public sealed class KeoBiaRealtimeHub : Hub
{
    public const string HubPath = "/keobia/hub";
    public const string PlayerJoinedMethod = "playerJoined";
    public const string ActivityAddedMethod = "activityAdded";
    public const string ActivityRemovedMethod = "activityRemoved";
    public const string ChatMessageMethod = "chatMessage";
}
