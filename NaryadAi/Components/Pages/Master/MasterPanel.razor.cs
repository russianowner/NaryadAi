using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Master
{
    public partial class MasterPanel : ComponentBase
    {
        [Inject] private AppDbContext DbContext { get; set; } = default!;
        [Inject] private ProtectedLocalStorage BrowserStorage { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        private bool isAuthorized = false;
        private int currentMasterId = 0;

        // Модель для заполнения формы
        private WorkOrder newOrder = new WorkOrder { Type = "Плановый", Priority = "Обычный" };

        // Списки для выпадающих меню
        private List<Equipment> equipmentList = new();
        private List<Employee> workers = new();

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
                    equipmentList = await DbContext.Equipments.OrderBy(e => e.Location).ToListAsync();
                    workers = await DbContext.Employees.Where(e => e.Role == "Worker").ToListAsync();

                    StateHasChanged();
                }
                else
                {
                    Navigation.NavigateTo("/login");
                }
            }
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

            // Генерируем уникальный номер и заполняем системные поля
            newOrder.Number = $"№{new Random().Next(1000, 9999)}";
            newOrder.Location = eq.Location;
            newOrder.Status = "Выдан";
            newOrder.MasterId = currentMasterId;
            newOrder.CreatedAt = DateTime.UtcNow;

            // По ТЗ аварийный - 2 часа, плановый - 24 часа
            newOrder.Deadline = newOrder.Priority == "Аварийный" ? DateTime.UtcNow.AddHours(2) : DateTime.UtcNow.AddHours(24);

            DbContext.WorkOrders.Add(newOrder);
            await DbContext.SaveChangesAsync();

            Snackbar.Add($"Наряд {newOrder.Number} успешно выдан!", Severity.Success);

            // Очищаем форму для следующего наряда
            newOrder = new WorkOrder { Type = "Плановый", Priority = "Обычный" };
        }
    }
}