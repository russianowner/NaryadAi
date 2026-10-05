namespace NaryadAi.Services;

public sealed record WorkOrderUpdate(int WorkOrderId, string Status, string? Message = null, bool Emergency = false);

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
            catch {  }
        }
    }
}
