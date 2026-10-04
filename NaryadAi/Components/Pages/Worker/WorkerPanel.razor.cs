using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
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
        [Inject] private Microsoft.JSInterop.IJSRuntime JSRuntime { get; set; } = default!;

        private bool isAuthorized = false;
        private int currentWorkerId = 0;
        private int refreshPending;
        private bool suppressNotifierRefresh;
        private bool disposed;
        private List<WorkOrder> myTasks = new();
        private List<ReferenceItem> materialItems = new();
        private List<ReferenceItem> faultCodes = new(); // РЎРїСЂР°РІРѕС‡РЅРёРє С€РёС„СЂРѕРІ

        // РЈРїСЂР°РІР»РµРЅРёРµ СЌРєСЂР°РЅР°РјРё: List (СЃРїРёСЃРѕРє), Reason (РѕС‚РєР°Р·/РїР°СѓР·Р°), CloseForm (РёСЃРїРѕР»РЅРµРЅРёРµ)
        private string currentView = "List";

        // Р’СЂРµРјРµРЅРЅС‹Рµ РїРµСЂРµРјРµРЅРЅС‹Рµ РґР»СЏ С„РѕСЂРј
        private WorkOrder? selectedTask;
        private string pendingStatus = "";
        private string actionReason = "";
        private string materialName = "";
        private decimal materialQuantity = 1;
        private string materialUnit = "С€С‚.";
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

                    // Р—Р°РіСЂСѓР¶Р°РµРј С€РёС„СЂС‹ РЅРµРёСЃРїСЂР°РІРЅРѕСЃС‚РµР№ РёР· СѓРЅРёРІРµСЂСЃР°Р»СЊРЅРѕРіРѕ СЃРїСЂР°РІРѕС‡РЅРёРєР°
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
                .Include(w => w.Equipment) // РџРѕРґС‚СЏРіРёРІР°РµРј РЅР°Р·РІР°РЅРёРµ РѕР±РѕСЂСѓРґРѕРІР°РЅРёСЏ
                .Include(w => w.Photos)
                .Include(w => w.Materials)
                .Where(n => n.ExecutorId == currentWorkerId && n.Status != "Р—Р°РєСЂС‹С‚" && n.Status != "РСЃРїРѕР»РЅРµРЅРѕ" && n.Status != "РџСЂРѕРІРµСЂРєР° РР" && n.Status != "РћС‚РєР»РѕРЅС‘РЅ")
                .OrderByDescending(n => n.Priority == "РђРІР°СЂРёР№РЅС‹Р№")
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
                    materialItems = await DbContext.ReferenceItems.Where(r => r.Category == "Material").OrderBy(r => r.Name).ToListAsync();
                    
                    var oldTaskIds = myTasks.Select(x => x.Id).ToHashSet();
                    await LoadMyTasks();
                    
                    var newTasks = myTasks.Where(x => !oldTaskIds.Contains(x.Id)).ToList();
                    foreach (var t in newTasks)
                    {
                        var isEmergency = t.Priority == "Аварийный";
                        await JSRuntime.InvokeVoidAsync(
                            "naryadNotifications.show",
                            $"Новый наряд: {t.Equipment?.Name ?? "Оборудование"}",
                            new { body = t.Description, data = isEmergency ? "emergency" : "normal" }
                        );
                    }
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
            Snackbar.Add($"РЎС‚Р°С‚СѓСЃ РёР·РјРµРЅРµРЅ РЅР°: {newStatus}", Severity.Info);
            await LoadMyTasks();
        }

        // --- Р›РћР“РРљРђ РћРўРљРђР—Рђ Р РџР РРћРЎРўРђРќРћР’РљР ---
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
                Snackbar.Add("РћР±СЏР·Р°С‚РµР»СЊРЅРѕ СѓРєР°Р¶РёС‚Рµ РїСЂРёС‡РёРЅСѓ!", Severity.Warning);
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

            Snackbar.Add($"РќР°СЂСЏРґ {pendingStatus.ToLower()}", Severity.Success);
            await ExitForms();
        }

        // --- Р›РћР“РРљРђ Р—РђРљР Р«РўРРЇ РќРђР РЇР”Рђ ---
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
                await PhotoService.SaveAsync(selectedTask!.Id, currentWorkerId, "РџРѕСЃР»Рµ", e.File);
                Snackbar.Add("Р¤РѕС‚Рѕ СЃРѕС…СЂР°РЅРµРЅРѕ", Severity.Success);
            }
            catch (InvalidOperationException ex)
            {
                Snackbar.Add(ex.Message, Severity.Warning);
            }
            catch (IOException)
            {
                Snackbar.Add("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕС…СЂР°РЅРёС‚СЊ С„РѕС‚Рѕ. РџСЂРѕРІРµСЂСЊС‚Рµ СЃРІРѕР±РѕРґРЅРѕРµ РјРµСЃС‚Рѕ Рё РїРѕРІС‚РѕСЂРёС‚Рµ Р·Р°РіСЂСѓР·РєСѓ.", Severity.Error);
            }
        }

        private void AddMaterial()
        {
            var material = materialItems.FirstOrDefault(x => x.Name == materialName);
            if (material is null || materialQuantity <= 0)
            {
                Snackbar.Add("Р’С‹Р±РµСЂРёС‚Рµ РјР°С‚РµСЂРёР°Р» РёР· СЃРїСЂР°РІРѕС‡РЅРёРєР° Рё СѓРєР°Р¶РёС‚Рµ РєРѕР»РёС‡РµСЃС‚РІРѕ.", Severity.Warning);
                return;
            }
            materialDraft.Add(new MaterialWriteOff { Material = material.Name, Quantity = materialQuantity, Unit = material.Unit });
            materialName = "";
            materialQuantity = 1;
            materialUnit = "РµРґ.";
        }

        private void UpdateMaterialUnit()
        {
            materialUnit = materialItems.FirstOrDefault(x => x.Name == materialName)?.Unit ?? "РµРґ.";
        }

        private void RemoveMaterial(MaterialWriteOff material) => materialDraft.Remove(material);

        private async Task ConfirmCloseTask()
        {
            // Р’Р°Р»РёРґР°С†РёСЏ РїРѕ РўР—: С„РѕС‚Рѕ РѕР±СЏР·Р°С‚РµР»СЊРЅРѕ РґР»СЏ РІРЅРµРїР»Р°РЅРѕРІС‹С… СЂР°Р±РѕС‚
            if (selectedTask!.Type == "Р’РЅРµРїР»Р°РЅРѕРІС‹Р№" && string.IsNullOrEmpty(selectedTask.PhotoAfterPath))
            {
                Snackbar.Add("Р”Р»СЏ Р°РІР°СЂРёР№РЅРѕРіРѕ РЅР°СЂСЏРґР° РѕР±СЏР·Р°С‚РµР»СЊРЅРѕ РїСЂРёРєСЂРµРїРёС‚Рµ С„РѕС‚Рѕ!", Severity.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedTask.CloseWorksDone) || string.IsNullOrWhiteSpace(selectedTask.CloseFaultCode))
            {
                Snackbar.Add("Р—Р°РїРѕР»РЅРёС‚Рµ РѕР±СЏР·Р°С‚РµР»СЊРЅС‹Рµ РїРѕР»СЏ (Р Р°Р±РѕС‚С‹ Рё РЁРёС„СЂ)", Severity.Warning);
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
                Snackbar.Add("РќР°СЂСЏРґ РѕС‚РїСЂР°РІР»РµРЅ РЅР° РїСЂРѕРІРµСЂРєСѓ РР", Severity.Success);
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
    }
}

