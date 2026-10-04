using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var activeOrders = await db.WorkOrders.Include(o => o.Equipment).Where(o => o.Status != "Закрыт" && o.Status != "Отклонён").ToListAsync(cancellationToken);
        var recentOrders = await db.WorkOrders.Include(o => o.Equipment).OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(cancellationToken);

        // Безопасное обезличивание персональных данных (ФИО) перед отправкой во внешний API (согласно разделу 9 ТЗ)
        var pseudonymMap = new Dictionary<string, string>(); // "Исполнитель #101" -> "Иванов И.И."
        var contextBuilder = new StringBuilder();

        contextBuilder.AppendLine("Текущие исполнители смены (обезличено для безопасности):");
        foreach (var w in workers)
        {
            var isBusy = activeOrders.Any(o => o.ExecutorId == w.Id);
            var pseudonym = $"Сотрудник_{w.Id} ({w.Specialty}, {w.Grade} разряд, {w.Brigade})";
            pseudonymMap[$"Сотрудник_{w.Id}"] = w.FullName;
            contextBuilder.AppendLine($"- {pseudonym}: Статус: {(isBusy ? "Занят нарядом" : "Свободен")}");
        }
        
        var overdue = activeOrders.Where(o => o.Deadline < DateTime.UtcNow).ToList();
        if (overdue.Count > 0)
        {
            contextBuilder.AppendLine($"\nПросроченные наряды ({overdue.Count} шт.):");
            foreach (var ov in overdue.Take(5))
            {
                contextBuilder.AppendLine($"- Наряд #{ov.Number}: Оборудование '{ov.Equipment?.Name}', исполнитель: Сотрудник_{ov.ExecutorId}, просрочен на {(int)(DateTime.UtcNow - ov.Deadline).TotalMinutes} мин.");
            }
        }

        contextBuilder.AppendLine("\nСтатистика поломок по оборудованию (за последние 100 нарядов):");
        var groups = recentOrders.Where(o => o.Equipment != null).GroupBy(o => o.Equipment!.Name).OrderByDescending(g => g.Count());
        foreach (var g in groups.Take(6))
        {
            var emergency = g.Count(o => o.Priority == "Аварийный" || o.Type == "Внеплановый");
            contextBuilder.AppendLine($"- '{g.Key}': всего нарядов {g.Count()} (аварийных: {emergency}).");
        }

        var systemPrompt = @"Ты — интеллектуальный ИИ-ассистент мастера смены на горно-обогатительном комбинате АО «Костанайские Минералы».
Отвечай профессионально, кратко, на русском языке, основываясь исключительно на переданных оперативных данных предприятия.
Если спрашивают кто свободен — перечисли свободных специалистов по профилю.
Если спрашивают про аварии или аномалии — укажи конкретные единицы оборудования с наибольшим числом поломок.
Используй обращения вида 'Сотрудник_ID', если они упомянуты в контексте.";

        var client = httpClientFactory.CreateClient(nameof(AiChatbotService));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {groqKey}");

        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = "qwen/qwen3.8-27b",
            max_tokens = 600,
            messages = new[] 
            { 
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Оперативные данные:\n{contextBuilder}\n\nВопрос мастера: {question}" } 
            }
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return "Ошибка связи с ИИ: " + response.StatusCode + " - " + err;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(jsonResponse);
        var answer = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "Нет ответа";

        // Деобезличивание: заменяем псевдонимы 'Сотрудник_X' на реальные имена для мастера
        foreach (var kvp in pseudonymMap)
        {
            answer = answer.Replace(kvp.Key, kvp.Value);
        }

        return answer;
    }
}
