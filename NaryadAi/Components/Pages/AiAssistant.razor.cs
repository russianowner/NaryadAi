using Microsoft.EntityFrameworkCore.Metadata.Internal;
using MudBlazor;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages
{
    public partial class AiAssistant
    {
        private List<ChatMessage> messages = new();
        private string inputMessage = "";
        private bool isTyping = false;

        protected override void OnInitialized()
        {
            Language.Changed += OnLanguageChanged;
            ResetGreeting();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender)
                return;

            var role = await BrowserStorage.GetAsync<string>("UserRole");
            var userId = await BrowserStorage.GetAsync<int>("UserId");

            if (!role.Success ||
                !userId.Success ||
                role.Value is not ("Master" or "Admin"))
            {
                Navigation.NavigateTo("/login", replace: true);
            }
        }

        private void ResetGreeting()
        {
            var greeting = Language.IsKazakh
                ? "Сәлеметсіз бе! Мен сіздің ЖИ-көмекшіңізбін. Нарядтар, жабдықтар мен қызметкерлер базасын талдаймын. Кім бос, қандай жабдық жиі бұзылады немесе материалдар шығыны туралы сұрай аласыз."
                : "Здравствуйте! Я ваш ИИ-ассистент. Я анализирую базу данных нарядов, оборудования и сотрудников. Вы можете спросить меня: кто сейчас свободен из слесарей, какие участки чаще ломаются, или попросить найти аномалии в расходе материалов.";
            if (!messages.Any())
            {
                messages.Add(new ChatMessage { Role = "assistant", Content = greeting });
            }
        }

        private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Language.Changed -= OnLanguageChanged;
        }

        private async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(inputMessage) && !isTyping)
            {
                await SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(inputMessage)) return;

            var userText = inputMessage.Trim();
            inputMessage = "";
            messages.Add(new ChatMessage { Role = "user", Content = userText });
            isTyping = true;
            StateHasChanged();

            try
            {
                var response = await ChatbotService.AskAsync(userText);
                messages.Add(new ChatMessage { Role = "assistant", Content = response });
            }
            catch (Exception ex)
            {
                Snackbar.Add("Ошибка соединения с ИИ: " + ex.Message, Severity.Error);
            }
            finally
            {
                isTyping = false;
                StateHasChanged();
            }
        }
    }
}
