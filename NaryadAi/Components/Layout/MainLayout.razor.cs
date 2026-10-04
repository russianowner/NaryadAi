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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await Language.InitializeAsync();
                StateHasChanged();
            }
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
            var next = Language.Current == "RU" ? "KZ" : "RU";
            await Language.SetAsync(next);
            StateHasChanged();
        }
    }
}
