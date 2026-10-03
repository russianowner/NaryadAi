using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class WorkOrderDeadlineMonitor(IServiceScopeFactory scopes, IHubContext<WorkOrdersHub> hub, ILogger<WorkOrderDeadlineMonitor> logger)
    : BackgroundService
{
    private static readonly string[] ActiveStates =
        [WorkOrderStates.Issued, WorkOrderStates.Accepted, WorkOrderStates.Queued, WorkOrderStates.InProgress, WorkOrderStates.Paused, WorkOrderStates.Rework];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Ошибка контроля сроков нарядов"); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var orders = await db.WorkOrders.Include(x => x.Events)
            .Where(x => ActiveStates.Contains(x.Status)).ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            string? action = null;
            string? comment = null;
            if (order.Status == WorkOrderStates.Issued && order.AcceptedAt is null)
            {
                var limit = order.Priority == "Аварийный" || order.Type == "Внеплановый" ? 3 : 10;
                if (now >= order.CreatedAt.AddMinutes(limit) && !order.Events.Any(x => x.Action == "SLA:accept-escalation"))
                {
                    action = "SLA:accept-escalation";
                    comment = $"Наряд не принят за {limit} минут. Эскалировано мастеру; требуется рассмотреть замену исполнителя.";
                }
            }
            if (action is null && order.Deadline > now && order.Deadline <= now.AddMinutes(30)
                && !order.Events.Any(x => x.Action == "SLA:deadline-reminder"))
            {
                action = "SLA:deadline-reminder";
                comment = "До срока выполнения осталось не более 30 минут.";
            }
            if (action is null && order.Deadline <= now && !order.Events.Any(x => x.Action == "SLA:overdue"))
            {
                action = "SLA:overdue";
                comment = "Срок наряда истёк. Уведомлены исполнитель и мастер.";
            }
            if (action is null) continue;

            var ev = new WorkOrderEvent { WorkOrderId = order.Id, ActorName = "Система контроля сроков", Action = action, OccurredAt = now, Comment = comment };
            db.WorkOrderEvents.Add(ev);
            order.Events.Add(ev);
            await hub.Clients.All.SendAsync("Notification", new
            {
                orderId = order.Id, order.Number, order.Priority,
                message = comment, emergency = order.Priority == "Аварийный" || order.Type == "Внеплановый", at = now
            }, cancellationToken);
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(cancellationToken);
    }
}
