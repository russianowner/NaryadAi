using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Profile;

public partial class Profile : ComponentBase
{
    [Inject] private AppDbContext DbContext { get; set; } = default!;
    [Inject] private ProtectedLocalStorage BrowserStorage { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private Employee? employee;
    private List<WorkOrder> recentOrders = new();
    private bool isLoaded;
    private string? errorMessage;
    private int totalOrders;
    private int activeOrders;
    private int completedOrders;
    private int overdueOrders;
    private int reworkOrders;

    private string Initials
    {
        get
        {
            if (employee is null) return "Н";
            var parts = employee.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
        }
    }

    private Color EmployeeStatusColor => employee?.Status switch
    {
        "Свободен" => Color.Success,
        "В работе" => Color.Warning,
        "В очереди" => Color.Info,
        "Не на смене" => Color.Default,
        _ => Color.Secondary
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            var idResult = await BrowserStorage.GetAsync<int>("UserId");
            var roleResult = await BrowserStorage.GetAsync<string>("UserRole");
            if (!idResult.Success || !roleResult.Success || idResult.Value <= 0)
            {
                Navigation.NavigateTo("/login");
                return;
            }

            employee = await DbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(person => person.Id == idResult.Value);

            if (employee is null || employee.Role != roleResult.Value)
            {
                Navigation.NavigateTo("/login");
                return;
            }

            await LoadOrdersAsync(employee);
        }
        catch (Exception)
        {
            errorMessage = "Не удалось загрузить профиль. Проверьте соединение с базой данных и войдите снова.";
        }
        finally
        {
            isLoaded = true;
            StateHasChanged();
        }
    }

    private async Task LoadOrdersAsync(Employee currentEmployee)
    {
        var query = DbContext.WorkOrders.AsNoTracking();
        query = currentEmployee.Role == "Master"
            ? query.Where(order => order.MasterId == currentEmployee.Id)
            : query.Where(order => order.ExecutorId == currentEmployee.Id);

        var orders = await query
            .Include(order => order.Equipment)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync();

        totalOrders = orders.Count;
        activeOrders = orders.Count(order => IsActive(order.Status));
        completedOrders = orders.Count(order => order.Status is "Исполнено" or "Закрыт");
        reworkOrders = orders.Count(order => order.Status == "На доработку");
        overdueOrders = orders.Count(order =>
            order.Deadline < DateTime.UtcNow &&
            order.Status is not ("Исполнено" or "Закрыт" or "Отклонен"));
        recentOrders = orders.Take(6).ToList();
    }

    private static bool IsActive(string status) => status is
        "Выдан" or "Принят в работу" or "В очереди" or "В работе" or "Приостановлен" or "На доработку";

    private static string RoleLabel(string role) => role switch
    {
        "Master" => "Мастер смены",
        "Worker" => "Исполнитель",
        "Manager" => "Руководитель",
        "Admin" => "Администратор",
        _ => role
    };

    private static string StatusLabel(string status) => string.IsNullOrWhiteSpace(status) ? "Статус не задан" : status;

    private static Color OrderStatusColor(string status) => status switch
    {
        "Исполнено" or "Закрыт" => Color.Success,
        "В работе" => Color.Warning,
        "Выдан" or "Принят в работу" => Color.Primary,
        "На доработку" or "Отклонен" => Color.Error,
        "В очереди" or "Приостановлен" => Color.Info,
        _ => Color.Default
    };
}
