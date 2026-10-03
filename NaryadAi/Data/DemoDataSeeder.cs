using Microsoft.EntityFrameworkCore;
using NaryadAi.Models;
using NaryadAi.Services;

namespace NaryadAi.Data;

/// <summary>
/// Seeds a realistic, deterministic demonstration dataset. It is deliberately opt-in
/// and refuses to mix sample data into a database that already contains site data.
/// </summary>
public static class DemoDataSeeder
{
    private const string MarkerInventoryNumber = "KM-DEMO-CV-07";

    private static readonly string[] SiteNames =
    [
        "Дробильный цех", "Обогатительный цех", "Ремонтно-механический участок", "Энергоучасток"
    ];

    private static readonly (string Name, string Type, string Criticality)[] EquipmentSpecs =
    [
        ("Щековая дробилка CR-01", "Дробилка", "Высокая"),
        ("Конвейер CV-03", "Конвейер", "Средняя"),
        ("Конвейер CV-05", "Конвейер", "Средняя"),
        ("Конвейер CV-07", "Конвейер", "Высокая"),
        ("Конвейер CV-12", "Конвейер", "Средняя"),
        ("Грохот SC-02", "Грохот", "Высокая"),
        ("Насос P-04", "Насос", "Высокая"),
        ("Насос P-06", "Насос", "Средняя"),
        ("Насос P-09", "Насос", "Средняя"),
        ("Редуктор GB-11", "Редуктор", "Высокая"),
        ("Двигатель M-14", "Электродвигатель", "Высокая"),
        ("Двигатель M-18", "Электродвигатель", "Средняя"),
        ("Компрессор AC-01", "Компрессор", "Высокая"),
        ("Вентилятор FN-03", "Вентилятор", "Средняя"),
        ("Трансформатор TR-02", "Трансформатор", "Высокая"),
        ("Автоматический выключатель QF-12", "Электрооборудование", "Средняя"),
        ("Станок токарный LT-04", "Станок", "Средняя"),
        ("Кран мостовой CRN-02", "Кран", "Высокая"),
        ("Редуктор GB-15", "Редуктор", "Средняя"),
        ("Насос P-12", "Насос", "Низкая"),
        ("Ленточный питатель BF-01", "Питатель", "Средняя"),
        ("Конвейер CV-18", "Конвейер", "Средняя"),
        ("Гидростанция HY-02", "Гидравлика", "Высокая"),
        ("Шкаф управления PLC-03", "Автоматика", "Высокая"),
        ("Осветительная линия LT-07", "Электрооборудование", "Низкая")
    ];

    private static readonly string[] WorkerNames =
    [
        "Александр Иванов", "Сергей Петров", "Дмитрий Соколов", "Андрей Смирнов", "Михаил Кузнецов",
        "Николай Попов", "Илья Васильев", "Евгений Павлов", "Павел Семёнов", "Роман Фёдоров",
        "Виктор Михайлов", "Олег Новиков", "Юрий Морозов", "Артём Волков", "Константин Лебедев"
    ];

    private static readonly string[] FaultCodes =
    [
        "FC-01 — Износ подшипника", "FC-02 — Нарушение центровки вала", "FC-03 — Перегрев привода конвейера",
        "FC-04 — Повреждение ленты", "FC-05 — Ослабление крепежа", "FC-06 — Утечка масла",
        "FC-07 — Снижение давления насоса", "FC-08 — Засорение фильтра", "FC-09 — Повышенная вибрация",
        "FC-10 — Неисправность электродвигателя", "FC-11 — Срабатывание защиты", "FC-12 — Обрыв кабеля",
        "FC-13 — Неисправность датчика", "FC-14 — Перегрев контактора", "FC-15 — Износ уплотнения",
        "FC-16 — Трещина сварного шва", "FC-17 — Разрегулировка тормоза", "FC-18 — Износ зубчатой передачи",
        "FC-19 — Неисправность системы охлаждения", "FC-20 — Посторонний шум механизма"
    ];

    private static readonly (string Name, string Unit)[] Materials =
    [
        ("Подшипник 6205", "шт."), ("Подшипник 6308", "шт."), ("Ремень клиновой B-1250", "шт."),
        ("Сальник 40×62×10", "шт."), ("Манжета гидравлическая", "шт."), ("Масло индустриальное И-40А", "л"),
        ("Смазка Литол-24", "кг"), ("Кабель ВВГнг 3×2,5", "м"), ("Предохранитель 10 A", "шт."),
        ("Контактор КМИ-22510", "шт."), ("Реле тепловое", "шт."), ("Датчик давления", "шт."),
        ("Прокладка паронитовая", "шт."), ("Болт М12×40", "шт."), ("Гайка М12", "шт."),
        ("Шайба 12 мм", "шт."), ("Электрод сварочный 3 мм", "кг"), ("Лента конвейерная", "м"),
        ("Фильтр масляный", "шт."), ("Фильтр воздушный", "шт."), ("Муфта упругая", "шт."),
        ("Шпонка 10×8", "шт."), ("Цепь приводная", "м"), ("Щётка электродвигателя", "шт."),
        ("Клеммник силовой", "шт."), ("Автоматический выключатель 16 A", "шт."), ("Масло редукторное", "л"),
        ("Уплотнительное кольцо", "шт."), ("Термодатчик", "шт."), ("Шланг гидравлический", "м"),
        ("Хомут металлический", "шт."), ("Краска защитная", "л"), ("Растворитель", "л"),
        ("Провод ПВ-3 2,5", "м"), ("Наконечник кабельный", "шт."), ("Пружина тормозная", "шт."),
        ("Зубчатое колесо", "шт."), ("Решётка грохота", "шт."), ("Фланцевая прокладка", "шт."),
        ("Смазочный ниппель", "шт.")
    ];

    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Equipments.AnyAsync(x => x.InventoryNumber == MarkerInventoryNumber, cancellationToken))
            return;

        var hasExistingData = await db.Sites.AnyAsync(cancellationToken)
            || await db.Equipments.AnyAsync(cancellationToken)
            || await db.WorkOrders.AnyAsync(cancellationToken)
            || await db.ReferenceItems.AnyAsync(cancellationToken)
            || await db.Employees.AnyAsync(x => x.Role != "Admin", cancellationToken);
        if (hasExistingData)
            throw new InvalidOperationException("Demo data seeding requires an empty database (except for the initial Admin account).");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var sites = SiteNames.Select(name => new Site { Name = name }).ToList();
        db.Sites.AddRange(sites);
        await db.SaveChangesAsync(cancellationToken);

        var equipment = EquipmentSpecs.Select((spec, index) => new Equipment
        {
            Name = spec.Name,
            Type = spec.Type,
            Criticality = spec.Criticality,
            InventoryNumber = index == 3 ? MarkerInventoryNumber : $"KM-DEMO-{index + 1:00}",
            SiteId = sites[index % sites.Count].Id,
            Location = sites[index % sites.Count].Name
        }).ToList();
        db.Equipments.AddRange(equipment);

        var masters = new List<Employee>
        {
            new() { FullName = "Ирина Васильева", Role = "Master", Specialty = "Мастер смены", Grade = 6, Brigade = "Смена 1", Shift = "Дневная", Status = "Свободен", PinCode = "1101" },
            new() { FullName = "Бауыржан Нуртаев", Role = "Master", Specialty = "Мастер смены", Grade = 6, Brigade = "Смена 2", Shift = "Ночная", Status = "Свободен", PinCode = "1102" }
        };
        var workers = WorkerNames.Select((name, index) => new Employee
        {
            FullName = name,
            Role = "Worker",
            Specialty = index % 3 == 0 ? "Слесарь-ремонтник" : index % 3 == 1 ? "Электромонтёр" : "Механик",
            Grade = 3 + index % 4,
            Brigade = $"Бригада {(char)('А' + index % 3)}",
            Shift = index % 2 == 0 ? "Дневная" : "Ночная",
            Status = "Свободен",
            PinCode = $"{4201 + index}"
        }).ToList();
        var manager = new Employee
        {
            FullName = "Данияр Ахметов", Role = "Manager", Specialty = "Начальник производства", Grade = 7,
            Brigade = string.Empty, Shift = "Дневная", Status = "Свободен", Login = "demo.manager",
            PasswordHash = "manager-demo-2026"
        };

        var references = new List<ReferenceItem>();
        references.AddRange(FaultCodes.Select(name => new ReferenceItem { Category = "FaultCode", Name = name }));
        references.AddRange(Materials.Select(x => new ReferenceItem { Category = "Material", Name = x.Name, Unit = x.Unit }));
        references.AddRange(new[] { "Дневная", "Ночная" }.Select(x => new ReferenceItem { Category = "Shift", Name = x }));
        references.AddRange(new[] { "Бригада А", "Бригада Б", "Бригада В" }.Select(x => new ReferenceItem { Category = "Brigade", Name = x }));
        references.AddRange(new[] { "Слесарь-ремонтник", "Электромонтёр", "Механик" }.Select(x => new ReferenceItem { Category = "Specialty", Name = x }));
        db.Employees.AddRange(masters);
        db.Employees.AddRange(workers);
        db.Employees.Add(manager);
        db.ReferenceItems.AddRange(references);
        await db.SaveChangesAsync(cancellationToken);

        var random = new Random(20261004);
        var now = DateTime.UtcNow;
        var orders = new List<WorkOrder>(500);
        for (var i = 0; i < 500; i++)
        {
            var status = i switch
            {
                < 380 => WorkOrderStates.Closed,
                < 400 => WorkOrderStates.Rework,
                < 415 => WorkOrderStates.AiReview,
                < 430 => WorkOrderStates.InProgress,
                < 440 => WorkOrderStates.Paused,
                < 460 => WorkOrderStates.Queued,
                < 470 => WorkOrderStates.Accepted,
                < 480 => WorkOrderStates.Rejected,
                _ => WorkOrderStates.Issued
            };

            var repeatedFailure = i < 80 && i % 2 == 0;
            var equipmentIndex = repeatedFailure ? 3 : random.Next(equipment.Count);
            var workerIndex = repeatedFailure ? 14 : random.Next(workers.Count);
            var faultIndex = repeatedFailure ? 2 : random.Next(FaultCodes.Length);
            var executor = workers[workerIndex];
            var master = masters[i % masters.Count];
            var isActive = status is WorkOrderStates.InProgress or WorkOrderStates.Paused
                or WorkOrderStates.Queued or WorkOrderStates.Accepted or WorkOrderStates.Issued;
            var createdAt = isActive
                ? now.AddHours(-random.Next(0, 72)).AddMinutes(-random.Next(0, 60))
                : now.AddDays(-random.Next(0, 89)).AddHours(-random.Next(0, 24));
            var priority = i % 37 == 0 ? "Аварийный" : i % 11 == 0 ? "Высокий" : i % 4 == 0 ? "Плановый" : "Обычный";
            var description = repeatedFailure
                ? "Повышенная вибрация и перегрев привода конвейера CV-07"
                : $"{FaultCodes[faultIndex]} на оборудовании {equipment[equipmentIndex].Name}";
            var order = new WorkOrder
            {
                Number = $"KM-{createdAt:yyMMdd}-{i + 1:0000}",
                Type = isActive && i % 3 == 0 ? "Внеплановый" : "Плановый",
                CreatedAt = createdAt,
                Deadline = createdAt.AddHours(priority == "Аварийный" ? 2 : priority == "Высокий" ? 8 : 24),
                Description = description,
                Priority = priority,
                Location = sites[equipmentIndex % sites.Count].Name,
                EquipmentId = equipment[equipmentIndex].Id,
                SiteId = equipment[equipmentIndex].SiteId,
                ExecutorId = executor.Id,
                MasterId = master.Id,
                Status = status,
                CloseFaultCode = status is WorkOrderStates.Closed or WorkOrderStates.Rework or WorkOrderStates.AiReview
                    ? FaultCodes[faultIndex] : null,
                CloseWorksDone = status is WorkOrderStates.Closed or WorkOrderStates.Rework or WorkOrderStates.AiReview
                    ? repeatedFailure
                        ? "Проверен привод конвейера CV-07, заменён подшипник, выполнена центровка и контрольный запуск."
                        : $"Проверено оборудование, устранена неисправность {FaultCodes[faultIndex]}, выполнен пробный пуск."
                    : null,
                CloseComment = status is WorkOrderStates.Closed or WorkOrderStates.Rework or WorkOrderStates.AiReview
                    ? "Рабочее место приведено в порядок, оборудование передано смене."
                    : null
            };
            orders.Add(order);
        }

        db.WorkOrders.AddRange(orders);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var executor = workers.Single(x => x.Id == order.ExecutorId);
            var master = masters.Single(x => x.Id == order.MasterId);
            AddEvent(order, master, WorkOrderStates.Issued, order.CreatedAt, "Наряд выдан исполнителю.");

            if (order.Status == WorkOrderStates.Rejected)
            {
                order.RejectedAt = order.CreatedAt.AddMinutes(5 + i % 50);
                AddEvent(order, executor, WorkOrderStates.Rejected, order.RejectedAt.Value, "Нет допуска к оборудованию в этой смене.");
                continue;
            }
            if (order.Status == WorkOrderStates.Issued) continue;
            if (order.Status == WorkOrderStates.Queued)
            {
                order.QueuedAt = order.CreatedAt.AddMinutes(2);
                AddEvent(order, executor, WorkOrderStates.Queued, order.QueuedAt.Value, "Принято в очередь работ.");
                continue;
            }

            order.AcceptedAt = order.CreatedAt.AddMinutes(2 + i % 8);
            AddEvent(order, executor, WorkOrderStates.Accepted, order.AcceptedAt.Value, "Наряд принят исполнителем.");
            if (order.Status == WorkOrderStates.Accepted) continue;

            order.StartedAt = order.AcceptedAt.Value.AddMinutes(2 + i % 15);
            AddEvent(order, executor, WorkOrderStates.InProgress, order.StartedAt.Value, "Работа начата.");
            if (order.Status == WorkOrderStates.InProgress) continue;
            if (order.Status == WorkOrderStates.Paused)
            {
                order.PausedAt = now.AddMinutes(-i % 90);
                AddEvent(order, executor, WorkOrderStates.Paused, order.PausedAt.Value, "Ожидание запасной части.");
                continue;
            }

            var completedAt = order.StartedAt.Value.AddHours(1 + i % 30);
            order.CompletedAt = completedAt;
            order.AiReviewStartedAt = completedAt;
            AddEvent(order, executor, WorkOrderStates.AiReview, completedAt, "Работа передана на проверку ИИ.");

            var score = order.Status switch
            {
                WorkOrderStates.Rework => 52m + i % 15,
                WorkOrderStates.AiReview => 68m + i % 12,
                _ => 84m + i % 17
            };
            order.AiEvaluations.Add(new AiEvaluation
            {
                Verdict = order.Status switch
                {
                    WorkOrderStates.Rework => "Требует доработки",
                    WorkOrderStates.AiReview => "Принято с замечаниями",
                    _ => "Принято"
                },
                Score = score,
                Explanation = order.Status == WorkOrderStates.Rework
                    ? "Уточните причину отказа и повторно приложите результат проверки."
                    : order.Status == WorkOrderStates.AiReview
                        ? "Работы выполнены, мастеру следует проверить замечания по материалам и срокам."
                        : "Выполненная работа соответствует описанию наряда.",
                EvaluatedAt = completedAt.AddMinutes(1),
                MasterApproved = order.Status == WorkOrderStates.Closed ? true : null,
                MasterComment = order.Status == WorkOrderStates.Closed ? "Проверено мастером смены." : null
            });

            var materialIndex = i % Materials.Length;
            order.Materials.Add(new MaterialWriteOff
            {
                Material = Materials[materialIndex].Name,
                Quantity = 1 + i % 3,
                Unit = Materials[materialIndex].Unit
            });
            if (i % 3 == 0)
            {
                order.Materials.Add(new MaterialWriteOff
                {
                    Material = Materials[(materialIndex + 6) % Materials.Length].Name,
                    Quantity = 1,
                    Unit = Materials[(materialIndex + 6) % Materials.Length].Unit
                });
            }

            if (order.Status == WorkOrderStates.Rework)
            {
                order.ReworkRequestedAt = completedAt.AddMinutes(2);
                AddEvent(order, null, WorkOrderStates.Rework, order.ReworkRequestedAt.Value, "Не приложено подтверждение контрольного запуска.");
            }
            else if (order.Status == WorkOrderStates.Closed)
            {
                if (i % 11 == 0)
                {
                    var reworkAt = completedAt.AddMinutes(2);
                    AddEvent(order, null, WorkOrderStates.Rework, reworkAt, "Нужно уточнить параметр контрольного запуска.");
                    AddEvent(order, executor, WorkOrderStates.Accepted, reworkAt.AddMinutes(3), "Доработка принята.");
                }
                order.ClosedAt = completedAt.AddMinutes(5 + i % 20);
                AddEvent(order, master, WorkOrderStates.Closed, order.ClosedAt.Value, "Мастер подтвердил выполнение.");
            }
        }

        foreach (var worker in workers)
        {
            var assigned = orders.Where(x => x.ExecutorId == worker.Id).ToList();
            if (assigned.Any(x => x.Status is WorkOrderStates.InProgress or WorkOrderStates.Paused))
                worker.Status = "В работе";
            else if (assigned.Any(x => x.Status is WorkOrderStates.Issued or WorkOrderStates.Accepted
                         or WorkOrderStates.Queued or WorkOrderStates.Rework))
                worker.Status = "В очереди";
            else
                worker.Status = "Свободен";
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddEvent(WorkOrder order, Employee? actor, string action, DateTime occurredAt, string? comment)
    {
        order.Events.Add(new WorkOrderEvent
        {
            ActorId = actor?.Id,
            ActorName = actor?.FullName ?? "Система проверки ИИ",
            Action = action,
            OccurredAt = occurredAt,
            Comment = comment
        });
    }
}
