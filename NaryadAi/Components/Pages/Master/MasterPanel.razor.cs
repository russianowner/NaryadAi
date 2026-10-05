using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages.Master
{
    public partial class MasterPanel : ComponentBase, IDisposable
    {
        [Inject] private AppDbContext DbContext { get; set; } = default!;
        [Inject] private ProtectedLocalStorage BrowserStorage { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private WorkOrderService WorkOrderService { get; set; } = default!;
        [Inject] private WorkOrderChangeNotifier ChangeNotifier { get; set; } = default!;
        [Inject] private AppLanguageService Language { get; set; } = default!;
        [Inject] private WorkOrderPhotoService PhotoService { get; set; } = default!;
        private bool isAuthorized = false;
        private int currentMasterId = 0;
        private int refreshPending;

        private bool isQrCameraOpen;
        private bool suppressNotifierRefresh;
        private bool disposed;
        private string? filterSite;
        private int? filterEquipment;
        private int? filterExecutor;
        private string? filterPriority;
        private int? recommendedWorkerId;
        private Microsoft.AspNetCore.Components.Forms.IBrowserFile? photoBefore;
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

        private List<Equipment> equipmentList = new();
        private List<Employee> workers = new();
        private List<Site> siteList = new();
        private List<Equipment> filteredEquipmentList = new();

        private int? selectedSiteId;
        private int? selectedEquipmentId;
        private List<WorkOrder> boardOrders = new();

        private IEnumerable<WorkOrder> FilteredOrders => boardOrders
            .Where(x => selectedStatus == "Все" || x.Status == selectedStatus)
            .Where(x => string.IsNullOrEmpty(filterSite) || x.Location == filterSite || x.Equipment?.Site?.Name == filterSite)
            .Where(x => filterEquipment == null || x.EquipmentId == filterEquipment)
            .Where(x => filterExecutor == null || x.ExecutorId == filterExecutor)
            .Where(x => string.IsNullOrEmpty(filterPriority) || x.Priority == filterPriority);
        private int MasterIssued => boardOrders.Count(x => x.CreatedAt >= DateTime.UtcNow.AddHours(-12));
        private int MasterCompleted => boardOrders.Count(x => x.CompletedAt >= DateTime.UtcNow.AddHours(-12));
        private int MasterOverdue => boardOrders.Count(x => x.Deadline < DateTime.UtcNow && x.ClosedAt == null);
        private int MasterIdle => boardOrders.Where(x => x.Status is "Выдан" or "В очереди" or "В работе" or "Приостановлен").Select(x => x.EquipmentId).Distinct().Count();

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var roleResult = await BrowserStorage.GetAsync<string>("UserRole");
                var idResult = await BrowserStorage.GetAsync<int>("UserId");
                if (roleResult.Success && (roleResult.Value == "Master" || roleResult.Value == "Admin") && idResult.Success)
                {
                    isAuthorized = true;
                    currentMasterId = idResult.Value;
                    ChangeNotifier.Changed += OnWorkOrderChanged;
                    await LoadData();
                    StateHasChanged();
                }
                else
                {
                    Navigation.NavigateTo("/login");
                }
            }
        }

        private void PredictDeadline()
        {
            if (string.IsNullOrWhiteSpace(newOrder.Description))
            {
                Snackbar.Add(Language.T("Сначала опишите проблему"), Severity.Warning);
                return;
            }

            var desc = newOrder.Description.ToLower();
            int hours = 24;
            string faultCodeSuggestion = "FC-05 — Ослабление крепежа"; 
            if (desc.Contains("течь") || desc.Contains("масл")) { hours = 2; faultCodeSuggestion = "FC-06 — Утечка масла"; }
            else if (desc.Contains("подшипник") || desc.Contains("замен") || desc.Contains("стук")) { hours = 4; faultCodeSuggestion = "FC-01 — Износ подшипника"; }
            else if (desc.Contains("кабел") || desc.Contains("электр") || desc.Contains("замыкание")) { hours = 3; faultCodeSuggestion = "FC-12 — Обрыв кабеля"; }
            else if (desc.Contains("вибрац")) { hours = 4; faultCodeSuggestion = "FC-09 — Повышенная вибрация"; }
            else if (newOrder.Priority == "Аварийный") { hours = 2; }

            deadlineLocal = DateTime.Now.AddHours(hours);
            Snackbar.Add($"{Language.T("ИИ предложил")}: {hours} {Language.T("ч")}. {Language.T("Вероятный шифр")}: {faultCodeSuggestion}", Severity.Info);
        }

        private async Task LoadData()
        {
            siteList = await DbContext.Sites
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();

            equipmentList = await DbContext.Equipments
                .AsNoTracking()
                .Include(e => e.Site)
                .OrderBy(e => e.Name)
                .ToListAsync();

            workers = await DbContext.Employees
                .AsNoTracking()
                .Where(e => e.Role == "Worker")
                .OrderBy(e => e.FullName)
                .ToListAsync();

            boardOrders = await DbContext.WorkOrders
                .AsNoTracking()
                .Include(x => x.Equipment)
                .Include(x => x.Executor)
                .Include(x => x.AiEvaluations)
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            filteredEquipmentList = new();
        }

        private void OnSiteSelected(int? siteId)
        {
            selectedSiteId = siteId;

            selectedEquipmentId = null;
            newOrder.EquipmentId = 0;
            newOrder.Location = string.Empty;

            if (selectedSiteId.HasValue)
            {
                filteredEquipmentList = equipmentList
                    .Where(e => e.SiteId == selectedSiteId.Value)
                    .OrderBy(e => e.Name)
                    .ToList();
            }
            else
            {
                filteredEquipmentList = new();
            }

            recommendedWorkerId = null;
        }

        private void OnWorkOrderChanged(WorkOrderUpdate update)
        {
            if (suppressNotifierRefresh || Interlocked.Exchange(ref refreshPending, 1) != 0)
                return;
            _ = InvokeAsync(async () =>
            {
                try
                {
                    await Task.Delay(50);
                    if (disposed || !isAuthorized)
                        return;
                    var relatedOrder = boardOrders.FirstOrDefault(x => x.Id == update.WorkOrderId);
                    await LoadData();
                    if (!string.IsNullOrWhiteSpace(update.Message) &&
                        (relatedOrder == null || relatedOrder.MasterId == currentMasterId))
                    {
                        await JSRuntime.InvokeVoidAsync(
                            "naryadNotifications.show",
                            update.Emergency
                                ? $"АВАРИЙНЫЙ наряд {update.WorkOrderId}"
                                : $"Контроль наряда {update.WorkOrderId}",
                            new
                            {
                                body = update.Message,
                                data = update.Emergency ? "emergency" : "normal"
                            });
                    }
                    StateHasChanged();
                }
                finally
                {
                    Interlocked.Exchange(ref refreshPending, 0);
                }
            });
        }

        private DotNetObjectReference<MasterPanel>? dotNetRef;
    
    protected override void OnInitialized()
    {
        dotNetRef = DotNetObjectReference.Create(this);
        Language.Changed += OnLanguageChanged;
    }
    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    private async Task StartVoiceInput()
    {
        try { await JSRuntime.InvokeVoidAsync("naryadHardware.startListening", dotNetRef, nameof(OnVoiceInputResult)); } catch {}
    }

    [JSInvokable]
    public void OnVoiceInputResult(string text)
    {
        newOrder.Description = (newOrder.Description + " " + text).Trim();
        StateHasChanged();
    }

        private async Task ScanQrCode()
        {
            isQrCameraOpen = true;
            StateHasChanged();

            try
            {
                await Task.Delay(100);

                await JSRuntime.InvokeVoidAsync(
                    "naryadHardware.startQrCamera");
            }
            catch (Exception ex)
            {
                isQrCameraOpen = false;

                Snackbar.Add(
                    $"Не удалось открыть камеру: {ex.Message}",
                    Severity.Error);

                StateHasChanged();
            }
        }

        private async Task CloseQrCamera()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync(
                    "naryadHardware.stopQrCamera");
            }
            catch
            {
                
            }

            isQrCameraOpen = false;
            StateHasChanged();
        }

        public void Dispose()
    {
        disposed = true;
        Language.Changed -= OnLanguageChanged;
        ChangeNotifier.Changed -= OnWorkOrderChanged;
    }

        private async Task CreateOrder()
        {
            if (string.IsNullOrWhiteSpace(newOrder.Description) || newOrder.EquipmentId == 0 || newOrder.ExecutorId == null)
            {
                Snackbar.Add(Language.T("Заполните описание, выберите оборудование и исполнителя"), Severity.Warning);
                return;
            }
            var eq = equipmentList.First(e => e.Id == newOrder.EquipmentId);
            newOrder.Location = eq.Site?.Name ?? eq.Location;
            newOrder.Deadline = DateTime.SpecifyKind(deadlineLocal, DateTimeKind.Local).ToUniversalTime();
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.CreateAsync(newOrder, currentMasterId);
                if (photoBefore != null)
                {
                    await PhotoService.SaveAsync(newOrder.Id, currentMasterId, "До", photoBefore);
                }
            }
            catch (InvalidOperationException ex)
            {
                Snackbar.Add(ex.Message, Severity.Warning);
                return;
            }
            finally
            {
                suppressNotifierRefresh = false;
            }

            Snackbar.Add($"{Language.T("Наряд")} {newOrder.Number} {Language.T("Наряд создан").ToLower()}!", Severity.Success);
            newOrder = new WorkOrder { Type = "Плановый", Priority = "Обычный" };
            deadlineLocal = DateTime.Now.AddHours(24);
            photoBefore = null; 
            await LoadData();
        }

        private async Task ApproveOrder(WorkOrder order)
        {
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.ReviewByMasterAsync(order.Id, currentMasterId, true, "Подтверждено мастером.");
                Snackbar.Add($"{Language.T("Наряд")} {order.Number} {Language.T("Закрыт").ToLower()}", Severity.Success);
                await LoadData();
            }
            catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
            finally { suppressNotifierRefresh = false; }
        }

        private async Task ReturnForRework(WorkOrder order)
        {
            var explanation = order.AiEvaluations.OrderByDescending(x => x.EvaluatedAt).FirstOrDefault()?.Explanation;
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.ReviewByMasterAsync(order.Id, currentMasterId, false,
                    string.IsNullOrWhiteSpace(explanation) ? "Требуется доработка по результату проверки." : explanation);
                Snackbar.Add($"{Language.T("Наряд")} {order.Number} {Language.T("возвращён исполнителю")}", Severity.Info);
                await LoadData();
            }
            catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
            finally { suppressNotifierRefresh = false; }
        }

        private readonly string[] priorities = ["Аварийный", "Высокий", "Обычный", "Плановый"];
        private readonly Dictionary<int, int?> reassignTargets = new();
        private readonly Dictionary<int, string> priorityTargets = new();

        private int? GetReassignTarget(int orderId) => reassignTargets.GetValueOrDefault(orderId);

        private void OnEquipmentSelected(int? eqId)
        {
            selectedEquipmentId = eqId;
            recommendedWorkerId = null;

            if (!eqId.HasValue)
            {
                newOrder.EquipmentId = 0;
                newOrder.Location = string.Empty;
                return;
            }

            var equipment = equipmentList
                .FirstOrDefault(e => e.Id == eqId.Value);

            if (equipment is null)
            {
                newOrder.EquipmentId = 0;
                newOrder.Location = string.Empty;
                return;
            }

            newOrder.EquipmentId = equipment.Id;
            newOrder.Location = equipment.Site?.Name ?? equipment.Location;

            var bestWorker = workers
                .Where(w => w.Status == "Свободен")
                .OrderByDescending(w => w.Grade)
                .FirstOrDefault();

            recommendedWorkerId = bestWorker?.Id;

            if (newOrder.ExecutorId == null && recommendedWorkerId.HasValue)
            {
                newOrder.ExecutorId = recommendedWorkerId.Value;

                Snackbar.Add(
                    Language.T("ИИ подобрал наиболее подходящего исполнителя"),
                    Severity.Info);
            }
        }

        private async Task ReassignOrder(WorkOrder order)
        {
            if (GetReassignTarget(order.Id) is not int newExecutorId)
            {
                Snackbar.Add(Language.T("Выберите нового исполнителя"), Severity.Warning);
                return;
            }
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.ReassignAsync(order.Id, currentMasterId, newExecutorId, "Переназначено мастером.");
                reassignTargets.Remove(order.Id);
                Snackbar.Add($"{Language.T("Наряд")} {order.Number} {Language.T("переназначен")}", Severity.Success);
                await LoadData();
            }
            catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
            finally { suppressNotifierRefresh = false; }
        }

        private async Task ChangePriority(WorkOrder order)
        {
            var priority = priorityTargets.GetValueOrDefault(order.Id) ?? order.Priority;
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.ChangePriorityAsync(order.Id, currentMasterId, priority, null);
                priorityTargets.Remove(order.Id);
                Snackbar.Add($"{Language.T("Приоритет")} {order.Number}: {Language.T(priority)}", Severity.Success);
                await LoadData();
            }
            catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
            finally { suppressNotifierRefresh = false; }
        }

        private void UploadPhotoBefore(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs e)
        {
            photoBefore = e.File;
            Snackbar.Add(Language.T("Фото до добавлено"), Severity.Info);
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

