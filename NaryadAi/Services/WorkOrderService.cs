using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class WorkOrderService(AppDbContext db, IHubContext<WorkOrdersHub> hub, AiReviewService aiReview)
{
    private static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>
    {
        [WorkOrderStates.Issued] = [WorkOrderStates.Accepted, WorkOrderStates.Queued, WorkOrderStates.Rejected],
        [WorkOrderStates.Accepted] = [WorkOrderStates.InProgress],
        [WorkOrderStates.Queued] = [WorkOrderStates.Accepted, WorkOrderStates.InProgress],
        [WorkOrderStates.InProgress] = [WorkOrderStates.Paused, WorkOrderStates.Completed],
        [WorkOrderStates.Paused] = [WorkOrderStates.InProgress],
        [WorkOrderStates.Rework] = [WorkOrderStates.Accepted, WorkOrderStates.InProgress, WorkOrderStates.Closed],
        [WorkOrderStates.Completed] = [WorkOrderStates.AiReview],
        [WorkOrderStates.AiReview] = [WorkOrderStates.Rework, WorkOrderStates.Closed]
    };

    public async Task<WorkOrder> CreateAsync(WorkOrder order, int masterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.Description) || order.EquipmentId <= 0 || order.ExecutorId is null)
            throw new InvalidOperationException("Нужно заполнить описание, оборудование и исполнителя.");

        var equipment = await db.Equipments.Include(x => x.Site).FirstOrDefaultAsync(x => x.Id == order.EquipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Оборудование не найдено.");
        var executor = await db.Employees.FirstOrDefaultAsync(x => x.Id == order.ExecutorId && x.Role == "Worker", cancellationToken)
            ?? throw new InvalidOperationException("Исполнитель не найден.");

        order.Number = $"НА-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid():N}"[..17].ToUpperInvariant();
        order.Status = WorkOrderStates.Issued;
        order.MasterId = masterId;
        order.CreatedAt = DateTime.UtcNow;
        order.Location = equipment.Site?.Name ?? equipment.Location;
        order.SiteId = equipment.SiteId;
        if (order.Deadline <= DateTime.UtcNow)
            order.Deadline = DateTime.UtcNow.AddHours(order.Priority == "Аварийный" ? 2 : 24);

        db.WorkOrders.Add(order);
        db.WorkOrderEvents.Add(new WorkOrderEvent
        {
            WorkOrder = order, ActorId = masterId, ActorName = "Мастер", Action = "Выдан",
            OccurredAt = order.CreatedAt, Comment = "Наряд назначен исполнителю."
        });
        executor.Status = "В работе";
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(order.Id, order.Status, cancellationToken);
        return order;
    }

    public async Task TransitionAsync(int orderId, string target, int? actorId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await db.WorkOrders.Include(x => x.Executor).FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");
        if (!Transitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(target))
            throw new InvalidOperationException($"Переход «{order.Status}» → «{target}» запрещён.");

        if ((target is WorkOrderStates.Rejected or WorkOrderStates.Paused or WorkOrderStates.Rework) && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Для этого действия обязательна причина.");

        var actor = actorId is null ? null : await db.Employees.FirstOrDefaultAsync(x => x.Id == actorId, cancellationToken);
        if ((target is WorkOrderStates.Closed or WorkOrderStates.Rework) && actor is not null && actor.Role is not ("Master" or "Admin" or "Manager"))
            throw new InvalidOperationException("Закрыть наряд или вернуть его может только мастер.");
        if ((target is WorkOrderStates.Accepted or WorkOrderStates.Queued or WorkOrderStates.Rejected or WorkOrderStates.InProgress or WorkOrderStates.Paused)
            && actor is not null && actor.Role != "Worker")
            throw new InvalidOperationException("Изменить статус исполнения может только исполнитель.");

        var now = DateTime.UtcNow;
        order.Status = target;
        switch (target)
        {
            case WorkOrderStates.Accepted: order.AcceptedAt = now; break;
            case WorkOrderStates.Queued: order.QueuedAt = now; break;
            case WorkOrderStates.InProgress: order.StartedAt ??= now; order.PausedAt = null; break;
            case WorkOrderStates.Paused: order.PausedAt = now; break;
            case WorkOrderStates.Rejected: order.RejectedAt = now; break;
            case WorkOrderStates.Rework: order.ReworkRequestedAt = now; break;
            case WorkOrderStates.Closed: order.ClosedAt = now; break;
        }

        db.WorkOrderEvents.Add(new WorkOrderEvent
        {
            WorkOrderId = orderId, ActorId = actorId, ActorName = actor?.FullName ?? "Система",
            Action = target, OccurredAt = now, Comment = reason?.Trim()
        });
        if (order.Executor is not null)
        {
            var activeStates = new[] { WorkOrderStates.Issued, WorkOrderStates.Accepted, WorkOrderStates.Queued,
                WorkOrderStates.InProgress, WorkOrderStates.Paused, WorkOrderStates.Rework };
            var executorHasActiveOrders = await db.WorkOrders.AnyAsync(x => x.ExecutorId == order.ExecutorId
                && x.Id != order.Id && activeStates.Contains(x.Status), cancellationToken);
            order.Executor.Status = target switch
            {
                WorkOrderStates.Queued when executorHasActiveOrders => "В работе",
                WorkOrderStates.Queued => "В очереди",
                WorkOrderStates.Closed or WorkOrderStates.Rejected when !executorHasActiveOrders => "Свободен",
                WorkOrderStates.Closed or WorkOrderStates.Rejected => "В работе",
                _ => "В работе"
            };
        }

        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(orderId, target, cancellationToken);
    }

    public async Task SubmitCompletionAsync(int orderId, int workerId, string worksDone, string faultCode,
        string? comment, CancellationToken cancellationToken = default)
    {
        var order = await db.WorkOrders.Include(x => x.Materials).Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == orderId && x.ExecutorId == workerId, cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден у этого исполнителя.");
        if (order.Status != WorkOrderStates.InProgress)
            throw new InvalidOperationException("Завершить можно только наряд в работе.");
        if (string.IsNullOrWhiteSpace(worksDone) || string.IsNullOrWhiteSpace(faultCode))
            throw new InvalidOperationException("Укажите выполненные работы и шифр неисправности.");
        if (order.Type == "Внеплановый" && !order.Photos.Any(x => x.Type == "После"))
            throw new InvalidOperationException("Для внепланового наряда требуется фото после выполнения.");

        order.CloseWorksDone = worksDone.Trim();
        order.CloseFaultCode = faultCode.Trim();
        order.CloseComment = comment?.Trim();
        order.CompletedAt = DateTime.UtcNow;
        order.AiReviewStartedAt = order.CompletedAt;
        order.Status = WorkOrderStates.AiReview;
        db.WorkOrderEvents.Add(new WorkOrderEvent
        {
            WorkOrderId = orderId, ActorId = workerId, ActorName = "Исполнитель",
            Action = WorkOrderStates.AiReview, OccurredAt = order.CompletedAt, Comment = "Работы отправлены на автоматическую проверку."
        });
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(orderId, order.Status, cancellationToken);

        var evaluation = await aiReview.ReviewAsync(order, cancellationToken);
        db.AiEvaluations.Add(new AiEvaluation
        {
            WorkOrderId = orderId, Verdict = evaluation.Verdict, Score = evaluation.Score,
            Explanation = evaluation.Explanation, EvaluatedAt = DateTime.UtcNow
        });
        if (evaluation.Verdict == "Требует доработки")
        {
            order.Status = WorkOrderStates.Rework;
            order.ReworkRequestedAt = DateTime.UtcNow;
            db.WorkOrderEvents.Add(new WorkOrderEvent
            {
                WorkOrderId = orderId, ActorName = "ИИ-проверка", Action = WorkOrderStates.Rework,
                OccurredAt = order.ReworkRequestedAt.Value, Comment = evaluation.Explanation
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(orderId, order.Status, cancellationToken);
    }

    public async Task ReviewByMasterAsync(int orderId, int masterId, bool approve, string? comment,
        CancellationToken cancellationToken = default)
    {
        var order = await db.WorkOrders.Include(x => x.AiEvaluations).FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");
        if (order.Status is not (WorkOrderStates.AiReview or WorkOrderStates.Rework))
            throw new InvalidOperationException("Мастер может проверить наряд только после ИИ-проверки.");
        var evaluation = order.AiEvaluations.OrderByDescending(x => x.EvaluatedAt).FirstOrDefault()
            ?? throw new InvalidOperationException("Оценка ИИ ещё не сформирована.");
        evaluation.MasterApproved = approve;
        evaluation.MasterComment = comment?.Trim();
        var target = approve ? WorkOrderStates.Closed : WorkOrderStates.Rework;
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("Укажите, что нужно исправить.");
        await TransitionAsync(orderId, target, masterId, comment, cancellationToken);
    }

    private Task PublishAsync(int orderId, string status, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("WorkOrderChanged", new { orderId, status, at = DateTime.UtcNow }, cancellationToken);
}
