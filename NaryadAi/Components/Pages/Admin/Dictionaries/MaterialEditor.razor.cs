using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Admin.Dictionaries;

public partial class MaterialEditor : ComponentBase
{
    [Inject]
    private AppDbContext DbContext { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;
    private List<ReferenceItem> materials = new();
    private string newMaterialName = string.Empty;
    private string newMaterialUnit = "шт.";

    protected override async Task OnInitializedAsync()
    {
        await LoadMaterials();
    }

    private async Task LoadMaterials()
    {
        materials = await DbContext.ReferenceItems
            .AsNoTracking()
            .Where(x => x.Category == "Material")
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    private async Task AddMaterial()
    {
        var name = newMaterialName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Snackbar.Add(
                "Введите название материала.",
                Severity.Warning);

            return;
        }
        if (string.IsNullOrWhiteSpace(newMaterialUnit))
        {
            Snackbar.Add(
                "Выберите единицу измерения.",
                Severity.Warning);

            return;
        }
        var exists = await DbContext.ReferenceItems.AnyAsync(
            x => x.Category == "Material" &&
                 x.Name.ToLower() == name.ToLower());
        if (exists)
        {
            Snackbar.Add(
                "Такой материал уже существует.",
                Severity.Warning);
            return;
        }
        DbContext.ReferenceItems.Add(new ReferenceItem
        {
            Category = "Material",
            Name = name,
            Unit = newMaterialUnit
        });
        await DbContext.SaveChangesAsync();
        newMaterialName = string.Empty;
        newMaterialUnit = "шт.";
        await LoadMaterials();
        Snackbar.Add(
            "Материал добавлен.",
            Severity.Success);
    }
    private async Task DeleteMaterial(ReferenceItem material)
    {
        DbContext.ReferenceItems.Remove(material);
        await DbContext.SaveChangesAsync();
        await LoadMaterials();
        Snackbar.Add(
            "Материал удалён.",
            Severity.Info);
    }
}