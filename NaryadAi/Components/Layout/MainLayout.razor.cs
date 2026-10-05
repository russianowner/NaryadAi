using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using NaryadAi.Services;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace NaryadAi.Components.Layout
{
    public partial class MainLayout : IDisposable
    {
        [Inject] public NavigationManager Nav { get; set; } = default!;
        [Inject] public ProtectedLocalStorage BrowserStorage { get; set; } = default!;

        private bool _drawerOpen = false;
        private string? userRole;
        private string? userName;
        private string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(userName)) return "Г";
                var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
            }
        }
        private string RoleName => userRole switch
        {
            "Master" => Language.T("Мастер смены"),
            "Worker" => Language.T("Исполнитель"),
            "Manager" => Language.T("Руководитель"),
            "Admin" => Language.T("Администратор"),
            _ => Language.T("Гость")
        };

        protected override void OnInitialized()
        {
            Language.Changed += StateHasChanged;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await Language.InitializeAsync();
                var roleResult = await BrowserStorage.GetAsync<string>("UserRole");
                var nameResult = await BrowserStorage.GetAsync<string>("UserName");
                if (roleResult.Success) userRole = roleResult.Value;
                if (nameResult.Success) userName = nameResult.Value;
                StateHasChanged();
            }
        }

        public void Dispose()
        {
            Language.Changed -= StateHasChanged;
        }

        private void ToggleDrawer()
        {
            _drawerOpen = !_drawerOpen;
        }

        private async Task ToggleLanguage()
        {
            var next = Language.Current == "RU" ? "KZ" : "RU";
            await Language.SetAsync(next);
            StateHasChanged();
        }
        private async Task Logout()
        {
            await BrowserStorage.DeleteAsync("UserId");
            await BrowserStorage.DeleteAsync("UserRole");
            await BrowserStorage.DeleteAsync("UserName");
            Nav.NavigateTo("/login", forceLoad: true);
        }
    }
}