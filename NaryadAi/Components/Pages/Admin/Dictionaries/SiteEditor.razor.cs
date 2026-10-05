using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Admin.Dictionaries;

public partial class SiteEditor : ComponentBase
{
    [Inject]
    private AppDbContext DbContext { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;
    private List<Site> sites = new();
    private string newSiteName = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await LoadSites();
    }

    private async Task LoadSites()
    {
        sites = await DbContext.Sites
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    private async Task AddSite()
    {
        var name = newSiteName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Snackbar.Add(
                "Введите название участка.",
                Severity.Warning);

            return;
        }
        var exists = await DbContext.Sites
            .AnyAsync(x => x.Name.ToLower() == name.ToLower());
        if (exists)
        {
            Snackbar.Add(
                "Такой участок уже существует.",
                Severity.Warning);

            return;
        }
        DbContext.Sites.Add(new Site
        {
            Name = name
        });
        await DbContext.SaveChangesAsync();
        newSiteName = string.Empty;
        await LoadSites();
        Snackbar.Add(
            "Участок добавлен.",
            Severity.Success);
    }

    private async Task DeleteSite(Site site)
    {
        var hasEquipment = await DbContext.Equipments
            .AnyAsync(x => x.SiteId == site.Id);
        if (hasEquipment)
        {
            Snackbar.Add(
                "Нельзя удалить участок, пока к нему привязано оборудование.",
                Severity.Warning);

            return;
        }
        DbContext.Sites.Remove(site);
        await DbContext.SaveChangesAsync();
        await LoadSites();
        Snackbar.Add(
            "Участок удалён.",
            Severity.Info);
    }
}