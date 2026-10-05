using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using NaryadAi.Data;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages.Login
{
    public partial class Login : ComponentBase
    {
        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Inject]
        private AppDbContext DbContext { get; set; } = default!;

        [Inject]
        private ProtectedLocalStorage BrowserStorage { get; set; } = default!;

        [Inject]
        private AppLanguageService Language { get; set; } = default!;

        private string enteredPin = "";
        private bool showError = false;

        private string username = "";
        private string password = "";
        private bool showLoginError = false;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await Language.InitializeAsync();
                StateHasChanged();
            }
        }

        private async Task OnLanguageChanged(string newLang)
        {
            await Language.SetAsync(newLang);
            StateHasChanged();
        }

        private Dictionary<string, string> ruTexts = new()
        {
            { "LoginTitle", "Вход - НарядAI" }, { "CompanyName", "АО «Костанайские минералы»" },
            { "PinTab", "ПИН-КОД" }, { "LoginTab", "ЛОГИН" },
            { "EnterPin", "Введите ПИН-код сотрудника" }, { "ErrorPin", "Неверный ПИН-код" },
            { "LoginLabel", "Табельный номер / Логин" }, { "PasswordLabel", "Пароль" },
            { "LoginButton", "ВОЙТИ" }, { "ErrorLogin", "Неверный логин или пароль" }
        };

        private Dictionary<string, string> kzTexts = new()
        {
            { "LoginTitle", "Кіру - НарядAI" }, { "CompanyName", "«Қостанай минералдары» АҚ" },
            { "PinTab", "ПИН-КОД" }, { "LoginTab", "ЛОГИН" },
            { "EnterPin", "Қызметкердің ПИН-кодын енгізіңіз" }, { "ErrorPin", "ПИН-код қате" },
            { "LoginLabel", "Табельдік нөмір / Логин" }, { "PasswordLabel", "Құпия сөз" },
            { "LoginButton", "КІРУ" }, { "ErrorLogin", "Логин немесе құпия сөз қате" }
        };

        private string GetText(string key) => Language.IsKazakh ? kzTexts[key] : ruTexts[key];

        private async Task AddDigit(string digit)
        {
            if (enteredPin.Length < 4)
            {
                enteredPin += digit;
                showError = false;

                if (enteredPin.Length == 4)
                {
                    await ValidatePin();
                }
            }
        }

        private void RemoveDigit()
        {
            if (enteredPin.Length > 0)
            {
                enteredPin = enteredPin.Substring(0, enteredPin.Length - 1);
                showError = false;
            }
        }

        private async Task ValidatePin()
        {
            var employee = await DbContext.Employees
                .FirstOrDefaultAsync(e => e.PinCode == enteredPin);
            if (employee != null)
            {
                await BrowserStorage.SetAsync("UserId", employee.Id);
                await BrowserStorage.SetAsync("UserRole", employee.Role);
                await BrowserStorage.SetAsync("UserName", employee.FullName);
                if (employee.Role == "Master")
                {
                    Navigation.NavigateTo("/master");
                }
                else if (employee.Role == "Worker")
                {
                    Navigation.NavigateTo("/worker");
                }
                else if (employee.Role == "Manager")
                {
                    Navigation.NavigateTo("/dashboard");
                }
                else if (employee.Role == "Admin")
                {
                    Navigation.NavigateTo("/admin/employees");
                }
                return;
            }
            showError = true;
            enteredPin = "";
        }

        private async Task ValidateLogin()
        {
            var user = await DbContext.Employees
                .FirstOrDefaultAsync(u => u.Login == username && u.PasswordHash == password);

            if (user != null)
            {
                await BrowserStorage.SetAsync("UserId", user.Id);
                await BrowserStorage.SetAsync("UserRole", user.Role);
                await BrowserStorage.SetAsync("UserName", user.FullName);

                if (user.Role == "Admin")
                    Navigation.NavigateTo("/admin/employees");
                else if (user.Role == "Manager")
                    Navigation.NavigateTo("/dashboard");
            }
            else
            {
                showLoginError = true;
            }
        }
    }
}