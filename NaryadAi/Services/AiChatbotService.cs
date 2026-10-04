using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

public class AiChatbotService(IHttpClientFactory httpClientFactory, IConfiguration configuration, AppDbContext db)
{
    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var groqKey = configuration["Ai:GroqApiKey"];
        if (string.IsNullOrWhiteSpace(groqKey))
            return "ИИ-модуль не настроен. Укажите Ai:GroqApiKey в настройках.";

        var workers = await db.Employees.Where(e => e.Role == "Worker").ToListAsync(cancellationToken);
        var activeOrders = await db.WorkOrders.Include(o => o.Equipment).Where(o => o.Status != "Закрыт" && o.Status != "Исполнено").ToListAsync(cancellationToken);
        var recentOrders = await db.WorkOrders.Include(o => o.Equipment).OrderByDescending(o => o.CreatedAt).Take(20).ToListAsync(cancellationToken);

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("Текущие исполнители на смене:");
        foreach (var w in workers)
        {
            var isBusy = activeOrders.Any(o => o.ExecutorId == w.Id);
            contextBuilder.AppendLine($"- {w.FullName} ({w.Specialty}): {(isBusy ? "Занят (в работе)" : "Свободен")}");
        }
        
        contextBuilder.AppendLine("\nАномалии и история (последние 20 нарядов):");
        var groups = recentOrders.GroupBy(o => o.Equipment?.Name ?? "Неизвестно").OrderByDescending(g => g.Count());
        foreach (var g in groups.Take(5))
        {
            contextBuilder.AppendLine($"- Оборудование '{g.Key}': {g.Count()} недавних нарядов (из них {g.Count(o => o.Priority == "Аварийный")} аварийных).");
        }

        var systemPrompt = @"Ты — ИИ-ассистент мастера на горно-обогатительном комбинате (на русском языке). 
Ты можешь анализировать загрузку сотрудников, находить аномалии (частые поломки оборудования), отвечать на вопросы мастера. 
Тебе предоставляется сводка по базе данных. Отвечай кратко, профессионально и только по делу, основываясь на данных. Не придумывай того, чего нет в контексте.
Форматируй ответ текстом (можно использовать списки).";

        var client = httpClientFactory.CreateClient(nameof(AiChatbotService));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {groqKey}");

        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = "qwen/qwen3.8-27b",
            max_tokens = 500,
            messages = new[] 
            { 
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Данные из БД:\n{contextBuilder}\n\nВопрос мастера: {question}" } 
            }
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return "Ошибка Groq API: " + response.StatusCode + " - " + err;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(jsonResponse);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "Нет ответа";
    }
}





