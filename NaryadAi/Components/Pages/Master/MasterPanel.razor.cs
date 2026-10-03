using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages.Master
{
    public partial class MasterPanel : ComponentBase
    {
        [Inject] private AppDbContext DbContext { get; set; } = default!;
        [Inject] private ProtectedLocalStorage BrowserStorage { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private WorkOrderService WorkOrderService { get; set; } = default!;

        private bool isAuthorized = false;
        private int currentMasterId = 0;

        // Модель для заполнения формы
        private WorkOrder newOrder = new WorkOrder { Type = "Плановый", Priority = "Обычный" };
        private DateTime deadlineLocal = DateTime.Now.AddHours(24);
        private string selectedStatus = "Все";
        private readonly string[] boardStatuses = ["Выдан", "Принят в работу", "В очереди", "В работе", "Приостановлен", "Проверка ИИ", "На доработку", "Закрыт", "Отклонён"];
        private readonly (string Title, string[] Statuses)[] boardLanes =
        [
            ("Выданы", ["Выдан", "Принят в работу", "В очереди"]),
            ("Исполняются", ["В работе", "Приостановлен", "На доработку"]),
            ("Проверка", ["Проверка ИИ"]),
            ("Завершены", ["Закрыт", "Отклонён"])
        ];

        // Списки для выпадающих меню
        private List<Equipment> equipmentList = new();
        private List<Employee> workers = new();
        private List<WorkOrder> boardOrders = new();

        private IEnumerable<WorkOrder> FilteredOrders => selectedStatus == "Все"
            ? boardOrders
            : boardOrders.Where(x => x.Status == selectedStatus);

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var roleResult = await BrowserStorage.GetAsync<string>("UserRole");
                var idResult = await BrowserStorage.GetAsync<int>("UserId");

                // Пускаем Мастера и Админа
                if (roleResult.Success && (roleResult.Value == "Master" || roleResult.Value == "Admin") && idResult.Success)
                {
                    isAuthorized = true;
                    currentMasterId = idResult.Value;

                    // Грузим списки из базы
                    await LoadData();

                    StateHasChanged();
                }
                else
                {
                    Navigation.NavigateTo("/login");
                }
            }
        }

        private async Task LoadData()
        {
            equipmentList = await DbContext.Equipments.Include(e => e.Site).OrderBy(e => e.Location).ToListAsync();
            workers = await DbContext.Employees.Where(e => e.Role == "Worker").OrderBy(e => e.FullName).ToListAsync();
            boardOrders = await DbContext.WorkOrders.Include(x => x.Equipment).Include(x => x.Executor)
                .Include(x => x.AiEvaluations).OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync();
        }

        private async Task CreateOrder()
        {
            // Базовая проверка
            if (string.IsNullOrWhiteSpace(newOrder.Description) || newOrder.EquipmentId == 0 || newOrder.ExecutorId == null)
            {
                Snackbar.Add("Заполните описание, выберите оборудование и исполнителя", Severity.Warning);
                return;
            }

            // Вытаскиваем Участок из выбранного оборудования (согласно ТЗ)
            var eq = equipmentList.First(e => e.Id == newOrder.EquipmentId);

            newOrder.Location = eq.Site?.Name ?? eq.Location;
            newOrder.Deadline = DateTime.SpecifyKind(deadlineLocal, DateTimeKind.Local).ToUniversalTime();
            try
            {
                await WorkOrderService.CreateAsync(newOrder, currentMasterId);
            }
            catch (InvalidOperationException ex)
            {
                Snackbar.Add(ex.Message, Severity.Warning);
                return;
            }

            Snackbar.Add($"Наряд {newOrder.Number} успешно выдан!", Severity.Success);

            // Очищаем форму для следующего наряда
            newOrder = new WorkOrder { Type = "Плановый", Priority = "Обычный" };
            deadlineLocal = DateTime.Now.AddHours(24);
            await LoadData();
        }

        private async Task ApproveOrder(WorkOrder order)
        {
            try
            {
                await WorkOrderService.ReviewByMasterAsync(order.Id, currentMasterId, true, "Подтверждено мастером.");
                Snackbar.Add($"Наряд {order.Number} закрыт", Severity.Success);
                await LoadData();
            }
            catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
        }

        private async Task ReturnForRework(WorkOrder order)
        {
            var explanation = order.AiEvaluations.OrderByDescending(x => x.EvaluatedAt).FirstOrDefault()?.Explanation;
            try
            {
                await WorkOrderService.ReviewByMasterAsync(order.Id, currentMasterId, false,
                    string.IsNullOrWhiteSpace(explanation) ? "Требуется доработка по результату проверки." : explanation);
                Snackbar.Add($"Наряд {order.Number} возвращён исполнителю", Severity.Info);
                await LoadData();
            }
            catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
        }

        private static string WorkerStatusClass(string status) => status switch
        {
            "Свободен" => "worker-status-free",
            "В работе" => "worker-status-busy",
            "В очереди" => "worker-status-queue",
            _ => "worker-status-off"
        };

        private static string StatusColor(string status) => status switch
        {
            "Закрыт" => "#2e7d32", "В работе" => "#ed9c18", "Отклонён" => "#d32f2f",
            "В очереди" => "#1976d2", "На доработку" => "#ed6c02", "Проверка ИИ" => "#7b1fa2",
            _ => "#607d8b"
        };
    }
}
