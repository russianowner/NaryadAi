using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class WorkOrderDeadlineMonitor(
    IServiceScopeFactory scopes,
    IHubContext<WorkOrdersHub> hub,
    WorkOrderChangeNotifier changeNotifier,
    ILogger<WorkOrderDeadlineMonitor> logger)
    : BackgroundService
{
    private const string Overdue = "SLA:overdue";
    private const string DeadlineReminder = "SLA:deadline-reminder";
    private const string AcceptEscalation = "SLA:accept-escalation";

    private static readonly string[] ActiveStates =
    [
        WorkOrderStates.Issued,
        WorkOrderStates.Accepted,
        WorkOrderStates.Queued,
        WorkOrderStates.InProgress,
        WorkOrderStates.Paused,
        WorkOrderStates.Rework
    ];

    private readonly HashSet<string> sentAlerts = new();

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer =
            new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOverdueAsync(stoppingToken);
                await CheckDeadlineReminderAsync(stoppingToken);
                await CheckAcceptEscalationAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Ошибка контроля сроков нарядов");
            }

            try
            {
                await timer.WaitForNextTickAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CheckOverdueAsync(
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var overdueOrders =
            await db.WorkOrders
                .Include(x => x.Equipment)
                .Include(x => x.Site)
                .Include(x => x.Executor)
                .Where(x =>
                    ActiveStates.Contains(x.Status) &&
                    x.Status != WorkOrderStates.Paused &&
                    x.Deadline <= now)
                .ToListAsync(cancellationToken);

        foreach (var order in overdueOrders)
        {
            var key =
                $"{order.Id}:overdue:{order.Deadline.Ticks}";

            if (sentAlerts.Contains(key))
                continue;

            var overdueText =
                $"Наряд №{order.Number} просрочен " +
                $"на {FormatOverdue(now - order.Deadline)}. " +
                $"{BuildDetails(order)}";

            var ev = new WorkOrderEvent
            {
                WorkOrderId = order.Id,
                ActorName = "Система контроля сроков",
                Action = Overdue,
                OccurredAt = now,
                Comment = overdueText
            };

            db.WorkOrderEvents.Add(ev);

            await db.SaveChangesAsync(cancellationToken);

            sentAlerts.Add(key);

            await NotifyAsync(
                order,
                ev,
                now,
                cancellationToken);
        }
    }

    private async Task CheckDeadlineReminderAsync(
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var reminderLimit = now.AddMinutes(30);

        var orders =
            await db.WorkOrders
                .Include(x => x.Equipment)
                .Include(x => x.Site)
                .Include(x => x.Executor)
                .Where(x =>
                    ActiveStates.Contains(x.Status) &&
                    x.Status != WorkOrderStates.Paused &&
                    x.Deadline > now &&
                    x.Deadline <= reminderLimit)
                .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            var key =
                $"{order.Id}:reminder:{order.Deadline.Ticks}";

            if (sentAlerts.Contains(key))
                continue;

            var minutesLeft = Math.Max(
                1,
                (int)Math.Ceiling(
                    (order.Deadline - now).TotalMinutes));

            var text =
                $"До срока по наряду №{order.Number} " +
                $"осталось {minutesLeft} мин. " +
                $"{BuildDetails(order)}";

            var ev = new WorkOrderEvent
            {
                WorkOrderId = order.Id,
                ActorName = "Система контроля сроков",
                Action = DeadlineReminder,
                OccurredAt = now,
                Comment = text
            };

            db.WorkOrderEvents.Add(ev);

            await db.SaveChangesAsync(cancellationToken);

            sentAlerts.Add(key);

            await NotifyAsync(
                order,
                ev,
                now,
                cancellationToken);
        }
    }

    private async Task CheckAcceptEscalationAsync(
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var orders =
            await db.WorkOrders
                .Include(x => x.Equipment)
                .Include(x => x.Site)
                .Include(x => x.Executor)
                .Where(x =>
                    x.Status == WorkOrderStates.Issued &&
                    x.AcceptedAt == null)
                .ToListAsync(cancellationToken);

        if (orders.Count == 0)
            return;

        var freeWorkers =
            await db.Employees
                .AsNoTracking()
                .Where(x =>
                    x.Role == "Worker" &&
                    x.Status == "Свободен")
                .OrderBy(x => x.FullName)
                .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            var emergency =
                order.Priority == "Аварийный" ||
                order.Type == "Внеплановый";

            var limit =
                emergency ? 3 : 10;

            if (now < order.CreatedAt.AddMinutes(limit))
                continue;

            var key =
                $"{order.Id}:accept:{order.CreatedAt.Ticks}";

            if (sentAlerts.Contains(key))
                continue;

            var substitute =
                freeWorkers.FirstOrDefault(
                    x => x.Id != order.ExecutorId);

            var advice =
                substitute is null
                    ? "Свободных исполнителей сейчас нет."
                    : $"Предлагаем передать: " +
                      $"{substitute.FullName} (свободен).";

            var text =
                $"Наряд №{order.Number} не принят " +
                $"за {limit} мин. " +
                $"{BuildDetails(order)} {advice}";

            var ev = new WorkOrderEvent
            {
                WorkOrderId = order.Id,
                ActorName = "Система контроля сроков",
                Action = AcceptEscalation,
                OccurredAt = now,
                Comment = text
            };

            db.WorkOrderEvents.Add(ev);

            await db.SaveChangesAsync(cancellationToken);

            sentAlerts.Add(key);

            await NotifyAsync(
                order,
                ev,
                now,
                cancellationToken);
        }
    }

    private async Task NotifyAsync(
        WorkOrder order,
        WorkOrderEvent ev,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var emergency =
            order.Priority == "Аварийный" ||
            order.Type == "Внеплановый";

        try
        {
            await hub.Clients.All.SendAsync(
                "Notification",
                new
                {
                    orderId = order.Id,
                    number = order.Number,
                    priority = order.Priority,
                    action = ev.Action,
                    message = ev.Comment,
                    emergency,
                    at = now
                },
                cancellationToken);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Не удалось отправить уведомление " +
                "по наряду {Number}",
                order.Number);
        }

        changeNotifier.Publish(
            new WorkOrderUpdate(
                order.Id,
                order.Status,
                ev.Comment,
                emergency));
    }

    private static string BuildDetails(WorkOrder order)
    {
        var site =
            order.Site?.Name ??
            order.Location;

        var worker =
            string.IsNullOrWhiteSpace(
                order.Executor?.FullName)
                    ? "не назначен"
                    : order.Executor.FullName;

        return
            $"{order.Equipment?.Name}, {site}. " +
            $"Исполнитель: {worker}. " +
            $"Статус: {order.Status.ToLowerInvariant()}.";
    }

    private static string FormatOverdue(
        TimeSpan span)
    {
        var minutes =
            Math.Max(1, (int)span.TotalMinutes);

        if (minutes < 60)
            return $"{minutes} мин";

        var hours = minutes / 60;
        var rest = minutes % 60;

        return rest == 0
            ? $"{hours} ч"
            : $"{hours} ч {rest} мин";
    }
}