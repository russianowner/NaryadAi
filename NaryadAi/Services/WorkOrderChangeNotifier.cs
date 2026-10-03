namespace NaryadAi.Services;

public sealed record WorkOrderUpdate(int WorkOrderId, string Status, string? Message = null, bool Emergency = false);

/// <summary>
/// In-process notifications refresh active Blazor circuits immediately. Blazor Server
/// transports these UI updates over its SignalR WebSocket connection.
/// </summary>
public sealed class WorkOrderChangeNotifier
{
    public event Action<WorkOrderUpdate>? Changed;

    public void Publish(WorkOrderUpdate update)
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (Action<WorkOrderUpdate> handler in handlers.GetInvocationList())
        {
            try { handler(update); }
            catch { /* A disconnected UI circuit must not fail the state change. */ }
        }
    }
}
