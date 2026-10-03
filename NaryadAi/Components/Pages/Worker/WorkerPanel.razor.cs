using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
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

        private bool isAuthorized = false;
        private int currentWorkerId = 0;
        private int refreshPending;
        private bool suppressNotifierRefresh;
        private bool disposed;
        private List<WorkOrder> myTasks = new();
        private List<ReferenceItem> materialItems = new();
        private List<ReferenceItem> faultCodes = new(); // Справочник шифров

        // Управление экранами: List (список), Reason (отказ/пауза), CloseForm (исполнение)
        private string currentView = "List";

        // Временные переменные для форм
        private WorkOrder? selectedTask;
        private string pendingStatus = "";
        private string actionReason = "";
        private string materialName = "";
        private decimal materialQuantity = 1;
        private string materialUnit = "шт.";
        private readonly List<MaterialWriteOff> materialDraft = new();

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

                    // Загружаем шифры неисправностей из универсального справочника
                    faultCodes = await DbContext.ReferenceItems.Where(r => r.Category == "FaultCode").ToListAsync();

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
                .Include(w => w.Equipment) // Подтягиваем название оборудования
                .Include(w => w.Photos)
                .Include(w => w.Materials)
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
                    if (disposed || !isAuthorized) return;
                    materialItems = await DbContext.ReferenceItems.Where(r => r.Category == "Material")
                        .OrderBy(r => r.Name).ToListAsync();
                    await LoadMyTasks();
                    StateHasChanged();
                }
                finally
                {
                    Interlocked.Exchange(ref refreshPending, 0);
                }
            });
        }

        public void Dispose()
        {
            disposed = true;
            ChangeNotifier.Changed -= OnWorkOrderChanged;
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
            Snackbar.Add($"Статус изменен на: {newStatus}", Severity.Info);
            await LoadMyTasks();
        }

        // --- ЛОГИКА ОТКАЗА И ПРИОСТАНОВКИ ---
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

        // --- ЛОГИКА ЗАКРЫТИЯ НАРЯДА ---
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
            // Валидация по ТЗ: фото обязательно для внеплановых работ
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
                WorkOrderId = selectedTask.Id, Material = x.Material, Quantity = x.Quantity, Unit = x.Unit
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
    }
}
