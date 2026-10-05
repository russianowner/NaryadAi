using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Admin.Dictionaries;

public partial class EquipmentEditor : ComponentBase
{
    [Inject]
    private AppDbContext DbContext { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    private List<Site> sites = new();
    private List<Equipment> equipment = new();

    private Equipment newEquipment = new()
    {
        Criticality = "Средняя"
    };

    private int? selectedSiteId;

    protected override async Task OnInitializedAsync()
    {
        await LoadData();
    }

    private async Task LoadData()
    {
        sites = await DbContext.Sites
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        equipment = await DbContext.Equipments
            .AsNoTracking()
            .Include(x => x.Site)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    private async Task AddEquipment()
    {
        if (string.IsNullOrWhiteSpace(newEquipment.Name))
        {
            Snackbar.Add(
                "Введите название оборудования.",
                Severity.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(newEquipment.InventoryNumber))
        {
            Snackbar.Add(
                "Введите инвентарный номер.",
                Severity.Warning);

            return;
        }

        if (!selectedSiteId.HasValue)
        {
            Snackbar.Add(
                "Выберите участок.",
                Severity.Warning);

            return;
        }

        var inventoryExists =
            await DbContext.Equipments.AnyAsync(
                x => x.InventoryNumber == newEquipment.InventoryNumber);

        if (inventoryExists)
        {
            Snackbar.Add(
                "Такой инвентарный номер уже существует.",
                Severity.Warning);

            return;
        }

        var site = await DbContext.Sites
            .FirstOrDefaultAsync(x => x.Id == selectedSiteId.Value);

        if (site is null)
        {
            Snackbar.Add(
                "Участок не найден.",
                Severity.Error);

            return;
        }

        var equipmentItem = new Equipment
        {
            Name = newEquipment.Name.Trim(),
            InventoryNumber = newEquipment.InventoryNumber.Trim(),
            Type = newEquipment.Type.Trim(),
            Criticality = newEquipment.Criticality,
            SiteId = site.Id,
            Location = site.Name
        };

        DbContext.Equipments.Add(equipmentItem);

        await DbContext.SaveChangesAsync();

        newEquipment = new Equipment
        {
            Criticality = "Средняя"
        };

        selectedSiteId = null;

        await LoadData();

        Snackbar.Add(
            "Оборудование добавлено.",
            Severity.Success);
    }

    private async Task DeleteEquipment(Equipment item)
    {
        DbContext.Equipments.Remove(item);

        await DbContext.SaveChangesAsync();
        await LoadData();

        Snackbar.Add(
            "Оборудование удалено.",
            Severity.Info);
    }
}