using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class WorkOrderDeadlineMonitor(IServiceScopeFactory scopes, IHubContext<WorkOrdersHub> hub,
    WorkOrderChangeNotifier changeNotifier, ILogger<WorkOrderDeadlineMonitor> logger)
    : BackgroundService
{
    private const string AcceptEscalation = "SLA:accept-escalation";
    private const string DeadlineReminder = "SLA:deadline-reminder";
    private const string Overdue = "SLA:overdue";

    private static readonly string[] ActiveStates =
        [WorkOrderStates.Issued, WorkOrderStates.Accepted, WorkOrderStates.Queued, WorkOrderStates.InProgress, WorkOrderStates.Paused, WorkOrderStates.Rework];

    private static readonly string[] SlaActions = [AcceptEscalation, DeadlineReminder, Overdue];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ошибка контроля сроков нарядов"); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private static bool IsEmergency(WorkOrder order) => order.Priority == "Аварийный" || order.Type == "Внеплановый";

    private static string ShortName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "не назначен";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? parts[0] : $"{parts[0]} {parts[1][0]}.";
    }

    private static string FormatOverdue(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)span.TotalMinutes);
        return minutes < 60 ? $"{minutes} мин" : $"{minutes / 60} ч {minutes % 60} мин";
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var orders = await db.WorkOrders.AsNoTracking().Include(x => x.Equipment).Include(x => x.Site).Include(x => x.Executor)
            .Where(x => ActiveStates.Contains(x.Status)).ToListAsync(cancellationToken);
        if (orders.Count == 0) return;
        var ids = orders.Select(x => x.Id).ToList();
        var existing = (await db.WorkOrderEvents.AsNoTracking()
                .Where(x => ids.Contains(x.WorkOrderId) && SlaActions.Contains(x.Action))
                .Select(x => new { x.WorkOrderId, x.Action }).ToListAsync(cancellationToken))
            .Select(x => (x.WorkOrderId, x.Action)).ToHashSet();

        var lastComments = (await db.WorkOrderEvents.AsNoTracking()
                .Where(x => ids.Contains(x.WorkOrderId) && x.Comment != null && x.Comment != "" && !x.Action.StartsWith("SLA:"))
                .Select(x => new { x.WorkOrderId, x.Comment, x.OccurredAt }).ToListAsync(cancellationToken))
            .GroupBy(x => x.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OccurredAt).First().Comment!);

        var freeWorkers = await db.Employees.AsNoTracking()
            .Where(x => x.Role == "Worker" && x.Status == "Свободен").OrderBy(x => x.FullName).ToListAsync(cancellationToken);

        var pending = new List<(WorkOrder Order, WorkOrderEvent Event)>();
        foreach (var order in orders)
        {
            bool Done(string a) => existing.Contains((order.Id, a));
            string Details()
            {
                var site = order.Site?.Name ?? order.Location;
                var startedPart = order.StartedAt is { } s ? $" с {s.ToLocalTime():HH:mm}" : string.Empty;
                var text = $"{order.Equipment?.Name}, {site}. Исполнитель: {ShortName(order.Executor?.FullName)}. Статус: {order.Status.ToLowerInvariant()}{startedPart}.";
                if (lastComments.TryGetValue(order.Id, out var last))
                    text += $" Последний комментарий: «{last}».";
                return text;
            }

            var emergency = IsEmergency(order);
            string? action = null;
            string? comment = null;

            if (order.Status == WorkOrderStates.Issued && order.AcceptedAt is null)
            {
                var limit = emergency ? 3 : 10;
                if (now >= order.CreatedAt.AddMinutes(limit) && !Done(AcceptEscalation))
                {
                    action = AcceptEscalation;
                    var substitute = freeWorkers.FirstOrDefault(w => w.Id != order.ExecutorId);
                    var advice = substitute is null
                        ? "Свободных исполнителей сейчас нет."
                        : $"Предлагаем передать: {substitute.FullName} (свободен).";
                    comment = $"Наряд №{order.Number} не принят за {limit} мин. {Details()} {advice}";
                }
            }
            if (action is null && order.Status != WorkOrderStates.Paused)
            {
                if (order.Deadline > now && order.Deadline <= now.AddMinutes(30) && !Done(DeadlineReminder))
                {
                    action = DeadlineReminder;
                    var left = Math.Max(1, (int)Math.Ceiling((order.Deadline - now).TotalMinutes));
                    comment = $"До срока по наряду №{order.Number} осталось {left} мин. {Details()}";
                }
                else if (order.Deadline <= now && !Done(Overdue))
                {
                    action = Overdue;
                    comment = $"Наряд №{order.Number} просрочен на {FormatOverdue(now - order.Deadline)}. {Details()}";
                }
            }
            if (action is null) continue;

            var ev = new WorkOrderEvent { WorkOrderId = order.Id, ActorName = "Система контроля сроков", Action = action, OccurredAt = now, Comment = comment };
            db.WorkOrderEvents.Add(ev);
            pending.Add((order, ev));
        }
        if (pending.Count == 0) return;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Не удалось сохранить SLA-события; повтор на следующем проходе");
            return;
        }

        foreach (var (order, ev) in pending)
        {
            var emergency = IsEmergency(order);
            try
            {
                await hub.Clients.All.SendAsync("Notification", new
                {
                    orderId = order.Id, order.Number, order.Priority,
                    message = ev.Comment, emergency, at = now
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Не удалось отправить SLA-уведомление по наряду {Number}", order.Number);
            }
            changeNotifier.Publish(new WorkOrderUpdate(order.Id, order.Status, ev.Comment, emergency));
        }
    }
}
