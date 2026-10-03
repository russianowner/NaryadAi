using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Admin.Dictionaries
{
    public partial class DictionaryEditor
    {
        [Parameter] public string Category { get; set; } = string.Empty;
        [Parameter] public string CategoryName { get; set; } = string.Empty;

        private List<ReferenceItem> items = new();
        private string newItemName = "";

        protected override async Task OnInitializedAsync()
        {
            await LoadItems();
        }

        private async Task LoadItems()
        {
            items = await DbContext.ReferenceItems
                .Where(r => r.Category == Category)
                .OrderBy(r => r.Name)
                .ToListAsync<ReferenceItem>(); 
        }

        private async Task AddItem()
        {
            if (string.IsNullOrWhiteSpace(newItemName)) return;

            if (items.Any(i => i.Name.ToLower() == newItemName.ToLower()))
            {
                Snackbar.Add("Такая запись уже существует!", Severity.Warning);
                return;
            }

            var item = new ReferenceItem { Category = Category, Name = newItemName };
            DbContext.ReferenceItems.Add(item);
            await DbContext.SaveChangesAsync();

            newItemName = "";
            await LoadItems();
            Snackbar.Add("Успешно добавлено", Severity.Success);
        }

        private async Task DeleteItem(ReferenceItem item)
        {
            DbContext.ReferenceItems.Remove(item);
            await DbContext.SaveChangesAsync();
            await LoadItems();
            Snackbar.Add("Запись удалена", Severity.Info);
        }
    }
}
