using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages.Worker
{
    public partial class WorkerPanel : ComponentBase, IDisposable
    {
        [Inject] private AppDbContext DbContext { get; set; } = default!;
        [Inject] private ProtectedLocalStorage BrowserStorage { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private WorkOrderService WorkOrderService { get; set; } = default!;
        [Inject] private WorkOrderPhotoService PhotoService { get; set; } = default!;
        [Inject] private WorkOrderChangeNotifier ChangeNotifier { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private AppLanguageService Language { get; set; } = default!;

        private bool isAuthorized = false;
        private int currentWorkerId = 0;
        private int refreshPending;
        private bool suppressNotifierRefresh;
        private bool disposed;
        private DotNetObjectReference<WorkerPanel>? dotNetRef;
        private List<WorkOrder> myTasks = new();
        private List<ReferenceItem> materialItems = new();
        private List<ReferenceItem> faultCodes = new(); 
        private string currentView = "List";
        private WorkOrder? selectedTask;
        private string pendingStatus = "";
        private string actionReason = "";
        private string materialName = "";
        private decimal materialQuantity = 1;
        private string materialUnit = "шт.";
        private readonly List<MaterialWriteOff> materialDraft = new();

        protected override void OnInitialized()
        {
            dotNetRef = DotNetObjectReference.Create(this);
            Language.Changed += OnLanguageChanged;
        }

        private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var roleResult = await BrowserStorage.GetAsync<string>("UserRole");
                var idResult = await BrowserStorage.GetAsync<int>("UserId");

                if (roleResult.Success && roleResult.Value == "Worker" && idResult.Success)
                {
                    isAuthorized = true;
                    currentWorkerId = idResult.Value;
                    ChangeNotifier.Changed += OnWorkOrderChanged;
                    faultCodes = await DbContext.ReferenceItems.Where(r => r.Category == "FaultCode").ToListAsync();
                    materialItems = await DbContext.ReferenceItems
                    .Where(r => r.Category == "Material")
                    .OrderBy(r => r.Name)
                    .ToListAsync();

                    await LoadMyTasks();
                    StateHasChanged();
                }
                else
                {
                    Navigation.NavigateTo("/login");
                }
            }
        }

        private async Task LoadMyTasks()
        {
            myTasks = await DbContext.WorkOrders.AsNoTracking()
                .Include(w => w.Equipment)
                .Include(w => w.Photos)
                .Include(w => w.Materials)
                .Include(w => w.AiEvaluations) 
                .Where(n => n.ExecutorId == currentWorkerId && n.Status != "Закрыт" && n.Status != "Исполнено" && n.Status != "Проверка ИИ" && n.Status != "Отклонён")
                .OrderByDescending(n => n.Priority == "Аварийный")
                .ThenBy(n => n.Deadline)
                .ToListAsync();
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

                    materialItems = await DbContext.ReferenceItems
                        .Where(r => r.Category == "Material")
                        .OrderBy(r => r.Name)
                        .ToListAsync();

                    var oldTaskIds = myTasks.Select(x => x.Id).ToHashSet();

                    await LoadMyTasks();

                    var newTasks = myTasks
                        .Where(x => !oldTaskIds.Contains(x.Id))
                        .ToList();

                    foreach (var task in newTasks)
                    {
                        var isEmergency =
                            task.Priority == "Аварийный" ||
                            task.Type == "Внеплановый";

                        await JSRuntime.InvokeVoidAsync(
                            "naryadNotifications.show",
                            $"Новый наряд: {task.Number}",
                            new
                            {
                                body = $"{task.Equipment?.Name ?? "Оборудование"}: {task.Description}",
                                data = isEmergency ? "emergency" : "normal"
                            });
                    }

                    if (!string.IsNullOrWhiteSpace(update.Message))
                    {
                        var relatedTask = myTasks.FirstOrDefault(x => x.Id == update.WorkOrderId);

                        if (relatedTask != null)
                        {
                            await JSRuntime.InvokeVoidAsync(
                                "naryadNotifications.show",
                                update.Emergency
                                    ? $"⚠ АВАРИЙНЫЙ наряд {relatedTask.Number}"
                                    : $"Наряд {relatedTask.Number}",
                                new
                                {
                                    body = update.Message,
                                    data = update.Emergency ? "emergency" : "normal"
                                });
                        }
                    }

                    StateHasChanged();
                }
                finally
                {
                    Interlocked.Exchange(ref refreshPending, 0);
                }
            });
        }
        private async Task StartVoiceInput()
        {
            try { await JSRuntime.InvokeVoidAsync("naryadHardware.startListening", dotNetRef, nameof(OnVoiceInputResult)); } catch { }
        }

        [JSInvokable]
        public void OnVoiceInputResult(string text)
        {
            if (selectedTask != null)
            {
                selectedTask.CloseWorksDone = (selectedTask.CloseWorksDone + " " + text).Trim();
                StateHasChanged();
            }
        }
        private async Task ChangeStatus(WorkOrder task, string newStatus)
        {
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.TransitionAsync(task.Id, newStatus, currentWorkerId);
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
            Snackbar.Add($"{Language.T("Статус изменён на")}: {Language.T(newStatus)}", Severity.Info);
            await LoadMyTasks();
        }
        private void OpenReasonForm(WorkOrder task, string status)
        {
            selectedTask = task;
            pendingStatus = status;
            actionReason = "";
            currentView = "Reason";
        }

        private async Task ConfirmReasonAction()
        {
            if (string.IsNullOrWhiteSpace(actionReason))
            {
                Snackbar.Add("Обязательно укажите причину!", Severity.Warning);
                return;
            }

            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.TransitionAsync(selectedTask!.Id, pendingStatus, currentWorkerId, actionReason);
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

            Snackbar.Add($"Наряд {pendingStatus.ToLower()}", Severity.Success);
            await ExitForms();
        }
        private void OpenCloseForm(WorkOrder task)
        {
            selectedTask = task;
            materialDraft.Clear();
            currentView = "CloseForm";
        }

        private async Task UploadPhoto(InputFileChangeEventArgs e)
        {
            try
            {
                await PhotoService.SaveAsync(selectedTask!.Id, currentWorkerId, "После", e.File);
                Snackbar.Add("Фото сохранено", Severity.Success);
            }
            catch (InvalidOperationException ex)
            {
                Snackbar.Add(ex.Message, Severity.Warning);
            }
            catch (IOException)
            {
                Snackbar.Add("Не удалось сохранить фото. Проверьте свободное место и повторите загрузку.", Severity.Error);
            }
        }

        private void AddMaterial()
        {
            var material = materialItems.FirstOrDefault(x => x.Name == materialName);
            if (material is null || materialQuantity <= 0)
            {
                Snackbar.Add("Выберите материал из справочника и укажите количество.", Severity.Warning);
                return;
            }
            materialDraft.Add(new MaterialWriteOff { Material = material.Name, Quantity = materialQuantity, Unit = material.Unit });
            materialName = "";
            materialQuantity = 1;
            materialUnit = "ед.";
        }

        private void UpdateMaterialUnit()
        {
            materialUnit = materialItems.FirstOrDefault(x => x.Name == materialName)?.Unit ?? "ед.";
        }

        private void RemoveMaterial(MaterialWriteOff material) => materialDraft.Remove(material);

        private async Task ConfirmCloseTask()
        {
            if (selectedTask!.Type == "Внеплановый" && string.IsNullOrEmpty(selectedTask.PhotoAfterPath))
            {
                Snackbar.Add("Для аварийного наряда обязательно прикрепите фото!", Severity.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedTask.CloseWorksDone) || string.IsNullOrWhiteSpace(selectedTask.CloseFaultCode))
            {
                Snackbar.Add("Заполните обязательные поля (Работы и Шифр)", Severity.Warning);
                return;
            }

            DbContext.MaterialWriteOffs.AddRange(materialDraft.Select(x => new MaterialWriteOff
            {
                WorkOrderId = selectedTask.Id,
                Material = x.Material,
                Quantity = x.Quantity,
                Unit = x.Unit
            }));
            await DbContext.SaveChangesAsync();
            suppressNotifierRefresh = true;
            try
            {
                await WorkOrderService.SubmitCompletionAsync(selectedTask.Id, currentWorkerId,
                    selectedTask.CloseWorksDone!, selectedTask.CloseFaultCode!, selectedTask.CloseComment);
                Snackbar.Add("Наряд отправлен на проверку ИИ", Severity.Success);
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
            await ExitForms();
        }

        private async Task ExitForms()
        {
            currentView = "List";
            selectedTask = null;
            await LoadMyTasks();
        }

        private void BackToList()
        {
            currentView = "List";
            selectedTask = null;
        }

        private async Task RequestNotificationPermission()
        {
            var granted = await JSRuntime.InvokeAsync<bool>("naryadNotifications.requestPermission");
            if (granted) Snackbar.Add("Уведомления включены", Severity.Success);
            else Snackbar.Add("Уведомления заблокированы в браузере", Severity.Warning);
        }

        public void Dispose()
        {
            disposed = true;
            Language.Changed -= OnLanguageChanged;
            ChangeNotifier.Changed -= OnWorkOrderChanged;
        }
    }
}