using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace NaryadAi.Components.Pages.Admin.Dictionaries
{
    public partial class Dictionaries : ComponentBase
    {
        private bool isAuthorized = false;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var roleResult = await BrowserStorage.GetAsync<string>("UserRole");

                if (roleResult.Success && roleResult.Value == "Admin")
                {
                    isAuthorized = true;
                    StateHasChanged();
                }
                else
                {
                    Navigation.NavigateTo("/login");
                }
            }
        }
    }
}