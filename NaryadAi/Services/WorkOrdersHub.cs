using Microsoft.AspNetCore.SignalR;

namespace NaryadAi.Services;

public class WorkOrdersHub : Hub
{
    // Hub intentionally exposes no client-selected employee groups. Events are broadcast
    // only to connected, already signed-in app sessions until server-side auth is migrated.
}
