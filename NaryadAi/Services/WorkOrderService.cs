using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class WorkOrderService(AppDbContext db, IHubContext<WorkOrdersHub> hub, AiReviewService aiReview,
    WorkOrderChangeNotifier changeNotifier)
{
    private static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>
    {
        [WorkOrderStates.Issued] =
        [WorkOrderStates.Accepted, WorkOrderStates.Queued, WorkOrderStates.Rejected],

        [WorkOrderStates.Accepted] =
        [WorkOrderStates.InProgress],

        [WorkOrderStates.Queued] =
        [WorkOrderStates.Accepted, WorkOrderStates.InProgress],

        [WorkOrderStates.InProgress] =
        [WorkOrderStates.Paused, WorkOrderStates.Completed],

        [WorkOrderStates.Paused] =
        [WorkOrderStates.InProgress],

        [WorkOrderStates.Rework] =
        [WorkOrderStates.Accepted, WorkOrderStates.InProgress],

        [WorkOrderStates.Completed] =
        [WorkOrderStates.AiReview],

        [WorkOrderStates.AiReview] =
        [WorkOrderStates.Rework, WorkOrderStates.Closed]
    };

    public async Task<WorkOrder> CreateAsync(WorkOrder order, int masterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.Description) || order.EquipmentId <= 0 || order.ExecutorId is null)
            throw new InvalidOperationException("Нужно заполнить описание, оборудование и исполнителя.");

        var master = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == masterId, cancellationToken)
            ?? throw new InvalidOperationException("\u041c\u0430\u0441\u0442\u0435\u0440 \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d.");
        if (master.Role is not ("Master" or "Admin" or "Manager"))
            throw new InvalidOperationException("\u0422\u043e\u043b\u044c\u043a\u043e \u043c\u0430\u0441\u0442\u0435\u0440, \u043c\u0435\u043d\u0435\u0434\u0436\u0435\u0440 \u0438\u043b\u0438 \u0430\u0434\u043c\u0438\u043d \u043c\u043e\u0436\u0435\u0442 \u0432\u044b\u0434\u0430\u0432\u0430\u0442\u044c \u043d\u0430\u0440\u044f\u0434\u044b.");

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
        var alreadyInProgress = await db.WorkOrders.AnyAsync(x => x.ExecutorId == executor.Id
            && (x.Status == WorkOrderStates.InProgress || x.Status == WorkOrderStates.Paused), cancellationToken);
        if (executor.Status != "\u041d\u0435 \u043d\u0430 \u0441\u043c\u0435\u043d\u0435")
            executor.Status = alreadyInProgress ? "\u0412 \u0440\u0430\u0431\u043e\u0442\u0435" : "\u0412 \u043e\u0447\u0435\u0440\u0435\u0434\u0438";
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(order.Id, order.Status, cancellationToken);
        return order;
    }

    public async Task TransitionAsync(int orderId, string target, int? actorId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM \"WorkOrders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .Include(x => x.Executor).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");
        if (!Transitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(target))
            throw new InvalidOperationException($"Переход «{order.Status}» → «{target}» запрещён.");

        if (actorId is null)
            throw new InvalidOperationException("\u0414\u043b\u044f \u0434\u0435\u0439\u0441\u0442\u0432\u0438\u044f \u043d\u0443\u0436\u0435\u043d \u0430\u043a\u0442\u0438\u0432\u043d\u044b\u0439 \u043f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u0435\u043b\u044c.");
        var validatedActor = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == actorId, cancellationToken)
            ?? throw new InvalidOperationException("\u041f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u0435\u043b\u044c \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d.");
        if (target is WorkOrderStates.Accepted or WorkOrderStates.Queued or WorkOrderStates.Rejected
            or WorkOrderStates.InProgress or WorkOrderStates.Paused)
        {
            if (validatedActor.Role != "Worker" || order.ExecutorId != validatedActor.Id)
                throw new InvalidOperationException("\u042d\u0442\u043e \u0434\u0435\u0439\u0441\u0442\u0432\u0438\u0435 \u0434\u043e\u0441\u0442\u0443\u043f\u043d\u043e \u0442\u043e\u043b\u044c\u043a\u043e \u043d\u0430\u0437\u043d\u0430\u0447\u0435\u043d\u043d\u043e\u043c\u0443 \u0438\u0441\u043f\u043e\u043b\u043d\u0438\u0442\u0435\u043b\u044e.");
        }
        else if (target is WorkOrderStates.Closed or WorkOrderStates.Rework)
        {
            if (validatedActor.Role is not ("Master" or "Admin" or "Manager")
                || validatedActor.Role == "Master" && order.MasterId != validatedActor.Id)
                throw new InvalidOperationException("\u042d\u0442\u043e\u0442 \u043d\u0430\u0440\u044f\u0434 \u043d\u0435 \u0434\u043e\u0441\u0442\u0443\u043f\u0435\u043d \u044d\u0442\u043e\u043c\u0443 \u043c\u0430\u0441\u0442\u0435\u0440\u0443.");
        }
        else
        {
            throw new InvalidOperationException("\u041d\u0435\u0434\u043e\u043f\u0443\u0441\u0442\u0438\u043c\u044b\u0439 \u0446\u0435\u043b\u0435\u0432\u043e\u0439 \u0441\u0442\u0430\u0442\u0443\u0441.");
        }

        if ((target is WorkOrderStates.Rejected or WorkOrderStates.Paused or WorkOrderStates.Rework) && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Для этого действия обязательна причина.");

        if (target == WorkOrderStates.InProgress && order.ExecutorId is not null
            && await db.WorkOrders.AnyAsync(x => x.ExecutorId == order.ExecutorId && x.Id != orderId
                && x.Status == WorkOrderStates.InProgress, cancellationToken))
            throw new InvalidOperationException("У исполнителя уже есть наряд в работе. Завершите или приостановите его.");

        var actor = validatedActor;

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
            await UpdateExecutorStatusAsync(order.Executor, order.Id, target, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await PublishAsync(orderId, target, cancellationToken);
    }

    public async Task SubmitCompletionAsync(int orderId, int workerId, string worksDone, string faultCode,
        string? comment, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM \"WorkOrders\" WHERE \"Id\" = {orderId} AND \"ExecutorId\" = {workerId} FOR UPDATE")
            .Include(x => x.Executor).Include(x => x.Materials).Include(x => x.Photos)
            .AsSplitQuery().FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден у этого исполнителя.");
        if (order.Executor?.Role != "Worker")
            throw new InvalidOperationException("\u0417\u0430\u0432\u0435\u0440\u0448\u0430\u0442\u044c \u043d\u0430\u0440\u044f\u0434 \u043c\u043e\u0436\u0435\u0442 \u0442\u043e\u043b\u044c\u043a\u043e \u0438\u0441\u043f\u043e\u043b\u043d\u0438\u0442\u0435\u043b\u044c.");
        if (order.Status != WorkOrderStates.InProgress)
            throw new InvalidOperationException("Завершить можно только наряд в работе.");
        if (string.IsNullOrWhiteSpace(worksDone) || string.IsNullOrWhiteSpace(faultCode))
            throw new InvalidOperationException("Укажите выполненные работы и шифр неисправности.");
        if (order.Type == "Внеплановый" && !order.Photos.Any(x => x.Type == "После"))
            throw new InvalidOperationException("Для внепланового наряда требуется фото после выполнения.");

        order.CloseWorksDone = worksDone.Trim();
        order.CloseFaultCode = faultCode.Trim();
        order.CloseComment = comment?.Trim();
        var completedAt = DateTime.UtcNow;
        order.CompletedAt = completedAt;
        order.AiReviewStartedAt = completedAt;
        order.Status = WorkOrderStates.AiReview;
        db.WorkOrderEvents.Add(new WorkOrderEvent
        {
            WorkOrderId = orderId, ActorId = workerId, ActorName = "Исполнитель",
            Action = WorkOrderStates.AiReview, OccurredAt = completedAt, Comment = "Работы отправлены на автоматическую проверку."
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
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
        var master = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == masterId, cancellationToken)
            ?? throw new InvalidOperationException("\u041c\u0430\u0441\u0442\u0435\u0440 \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d.");
        if (master.Role is not ("Master" or "Admin" or "Manager")
            || master.Role == "Master" && order.MasterId != master.Id)
            throw new InvalidOperationException("\u042d\u0442\u043e\u0442 \u043d\u0430\u0440\u044f\u0434 \u043d\u0435 \u0434\u043e\u0441\u0442\u0443\u043f\u0435\u043d \u044d\u0442\u043e\u043c\u0443 \u043c\u0430\u0441\u0442\u0435\u0440\u0443.");
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

    private static readonly string[] ReassignableStates =
        [WorkOrderStates.Issued, WorkOrderStates.Accepted, WorkOrderStates.Queued, WorkOrderStates.Rejected,
         WorkOrderStates.Paused, WorkOrderStates.Rework];

    private async Task<Employee> RequireMasterAsync(WorkOrder order, int masterId, CancellationToken cancellationToken)
    {
        var master = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == masterId, cancellationToken)
            ?? throw new InvalidOperationException("Мастер не найден.");
        if (master.Role is not ("Master" or "Admin" or "Manager")
            || master.Role == "Master" && order.MasterId != master.Id)
            throw new InvalidOperationException("Этот наряд недоступен этому мастеру.");
        return master;
    }
    public async Task ReassignAsync(int orderId, int masterId, int newExecutorId, string? reason,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM \"WorkOrders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .Include(x => x.Executor).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");
        var master = await RequireMasterAsync(order, masterId, cancellationToken);
        if (!ReassignableStates.Contains(order.Status))
            throw new InvalidOperationException($"Наряд в статусе «{order.Status}» переназначить нельзя.");
        if (order.ExecutorId == newExecutorId)
            throw new InvalidOperationException("Наряд уже назначен на этого исполнителя.");
        var newExecutor = await db.Employees.FirstOrDefaultAsync(x => x.Id == newExecutorId && x.Role == "Worker", cancellationToken)
            ?? throw new InvalidOperationException("Исполнитель не найден.");

        var oldExecutor = order.Executor;
        var oldName = oldExecutor?.FullName ?? "не назначен";
        var now = DateTime.UtcNow;
        order.ExecutorId = newExecutor.Id;
        order.Executor = newExecutor;
        order.Status = WorkOrderStates.Issued;
        order.AcceptedAt = null;
        order.QueuedAt = null;
        order.PausedAt = null;
        order.RejectedAt = null;
        db.WorkOrderEvents.Add(new WorkOrderEvent
        {
            WorkOrderId = orderId, ActorId = masterId, ActorName = master.FullName, Action = "Переназначен", OccurredAt = now,
            Comment = $"{oldName} → {newExecutor.FullName}" + (string.IsNullOrWhiteSpace(reason) ? "" : $". Причина: {reason.Trim()}")
        });
        await db.SaveChangesAsync(cancellationToken);
        if (oldExecutor is not null)
            await UpdateExecutorStatusAsync(oldExecutor, orderId, WorkOrderStates.Closed, cancellationToken);
        await UpdateExecutorStatusAsync(newExecutor, orderId, WorkOrderStates.Issued, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await PublishAsync(orderId, order.Status, cancellationToken);
    }
    public async Task ChangePriorityAsync(int orderId, int masterId, string priority, string? reason,
        CancellationToken cancellationToken = default)
    {
        if (priority is not ("Аварийный" or "Высокий" or "Обычный" or "Плановый"))
            throw new InvalidOperationException("Недопустимый приоритет.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM \"WorkOrders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");
        var master = await RequireMasterAsync(order, masterId, cancellationToken);
        if (!ReassignableStates.Contains(order.Status) && order.Status != WorkOrderStates.InProgress)
            throw new InvalidOperationException($"В статусе «{order.Status}» приоритет менять нельзя.");
        if (order.Priority == priority) return;
        var old = order.Priority;
        order.Priority = priority;
        db.WorkOrderEvents.Add(new WorkOrderEvent
        {
            WorkOrderId = orderId, ActorId = masterId, ActorName = master.FullName, Action = "Приоритет изменён", OccurredAt = DateTime.UtcNow,
            Comment = $"{old} → {priority}" + (string.IsNullOrWhiteSpace(reason) ? "" : $". Причина: {reason.Trim()}")
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await PublishAsync(orderId, order.Status, cancellationToken);
    }

    private async Task PublishAsync(int orderId, string status, CancellationToken cancellationToken)
    {
        await hub.Clients.All.SendAsync("WorkOrderChanged", new { orderId, status, at = DateTime.UtcNow }, cancellationToken);
        changeNotifier.Publish(new WorkOrderUpdate(orderId, status));
    }

    private async Task UpdateExecutorStatusAsync(Employee executor, int changedOrderId, string changedStatus,
        CancellationToken cancellationToken)
    {
        if (executor.Status == "\u041d\u0435 \u043d\u0430 \u0441\u043c\u0435\u043d\u0435") return;
        var activeStates = new[] { WorkOrderStates.Issued, WorkOrderStates.Accepted, WorkOrderStates.Queued,
            WorkOrderStates.InProgress, WorkOrderStates.Paused, WorkOrderStates.Rework };
        var otherStatuses = await db.WorkOrders.AsNoTracking()
            .Where(x => x.ExecutorId == executor.Id && x.Id != changedOrderId && activeStates.Contains(x.Status))
            .Select(x => x.Status)
            .ToListAsync(cancellationToken);
        var activeStatuses = otherStatuses.Append(changedStatus).Where(activeStates.Contains).ToList();
        executor.Status = activeStatuses.Any(x => x is WorkOrderStates.InProgress or WorkOrderStates.Paused)
            ? "\u0412 \u0440\u0430\u0431\u043e\u0442\u0435"
            : activeStatuses.Count > 0 ? "\u0412 \u043e\u0447\u0435\u0440\u0435\u0434\u0438" : "\u0421\u0432\u043e\u0431\u043e\u0434\u0435\u043d";
    }
}
