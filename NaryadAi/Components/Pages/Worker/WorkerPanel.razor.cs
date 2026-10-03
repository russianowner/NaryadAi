using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Worker
{
    public partial class WorkerPanel : ComponentBase
    {
        [Inject] private AppDbContext DbContext { get; set; } = default!;
        [Inject] private ProtectedLocalStorage BrowserStorage { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        private bool isAuthorized = false;
        private int currentWorkerId = 0;
        private List<WorkOrder> myTasks = new();
        private List<ReferenceItem> faultCodes = new(); // Справочник шифров

        // Управление экранами: List (список), Reason (отказ/пауза), CloseForm (исполнение)
        private string currentView = "List";

        // Временные переменные для форм
        private WorkOrder? selectedTask;
        private string pendingStatus = "";
        private string actionReason = "";

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
            myTasks = await DbContext.WorkOrders
                .Include(w => w.Equipment) // Подтягиваем название оборудования
                .Where(n => n.ExecutorId == currentWorkerId && n.Status != "Закрыт" && n.Status != "Исполнено")
                .OrderByDescending(n => n.Priority == "Аварийный")
                .ThenBy(n => n.Deadline)
                .ToListAsync();
        }

        private async Task ChangeStatus(WorkOrder task, string newStatus)
        {
            task.Status = newStatus;
            DbContext.WorkOrders.Update(task);
            await DbContext.SaveChangesAsync();
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

            // Записываем причину в комментарий наряда
            selectedTask!.CloseComment = $"[{pendingStatus}] Причина: {actionReason}";
            selectedTask.Status = pendingStatus;

            DbContext.WorkOrders.Update(selectedTask);
            await DbContext.SaveChangesAsync();

            Snackbar.Add($"Наряд {pendingStatus.ToLower()}", Severity.Success);
            await ExitForms();
        }

        // --- ЛОГИКА ЗАКРЫТИЯ НАРЯДА ---
        private void OpenCloseForm(WorkOrder task)
        {
            selectedTask = task;
            currentView = "CloseForm";
        }

        private void UploadPhoto(InputFileChangeEventArgs e)
        {
            // Для MVP просто имитируем загрузку и сохраняем имя файла
            selectedTask!.PhotoAfterPath = e.File.Name;
            Snackbar.Add("Фото успешно прикреплено", Severity.Success);
        }

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

            selectedTask.Status = "Исполнено";
            DbContext.WorkOrders.Update(selectedTask);
            await DbContext.SaveChangesAsync();

            Snackbar.Add("Наряд отправлен на проверку ИИ", Severity.Success);
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