using Microsoft.AspNetCore.Components;
using NaryadAi.Services;
using System.Threading.Tasks;
using System;

namespace NaryadAi.Components.Layout
{
    public partial class MainLayout : IDisposable
    {
        [Inject] public NavigationManager Nav { get; set; } = default!;

        bool _drawerOpen = false;

        protected override void OnInitialized()
        {
            Language.Changed += StateHasChanged;
        }

        public void Dispose()
        {
            Language.Changed -= StateHasChanged;
        }

        void ToggleDrawer()
        {
            _drawerOpen = !_drawerOpen;
        }

        async Task ToggleLanguage()
        {
            await Language.SetAsync(Language.Current == "RU" ? "KZ" : "RU");
            Nav.NavigateTo(Nav.Uri, forceLoad: true);
        }
    }
}
