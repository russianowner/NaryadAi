using System.Text;
using System.Text.Json;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class AiAnalyticsService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    public async Task<string> GenerateShiftSummaryAsync(IReadOnlyList<WorkOrder> orders, CancellationToken cancellationToken = default)
    {
        var groqKey = configuration["Ai:GroqApiKey"];
        if (string.IsNullOrWhiteSpace(groqKey))
        {
            return "ИИ-аналитика недоступна (не настроен API ключ Groq). За выбранный период обработано нарядов: " + orders.Count;
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(AiAnalyticsService));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {groqKey}");

            var stats = new StringBuilder();
            stats.AppendLine("Всего нарядов: " + orders.Count);
            stats.AppendLine("Аварийных: " + orders.Count(x => x.Priority == "Аварийный"));
            stats.AppendLine("Закрыто: " + orders.Count(x => x.ClosedAt != null));
            stats.AppendLine("Отклонено: " + orders.Count(x => x.Status == "Отклонён"));
            
            var equipmentFaults = orders.Where(x => x.Equipment != null)
                .GroupBy(x => x.Equipment!.Name)
                .Select(g => $"{g.Key}: {g.Count()} раз")
                .ToList();
            
            stats.AppendLine("Поломки по оборудованию: " + string.Join(", ", equipmentFaults.Take(10)));

            var systemPrompt = "Ты — аналитический ИИ для горно-обогатительного комбината. Твоя задача — сделать краткий (2-4 предложения) вывод по статистике нарядов за смену. Напиши, стабильна ли ситуация, какие узлы ломались чаще всего, и дай одну короткую рекомендацию. Отвечай только текстом без форматирования.";
            
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = "qwen/qwen3.8-27b",
                max_tokens = 300,
                messages = new[] 
                { 
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = "Статистика смены:\n" + stats.ToString() } 
                }
            }), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(jsonResponse);
            var text = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "Нет данных";
            return text;
        }
        catch (Exception ex)
        {
            return "Не удалось сгенерировать сводку из-за ошибки сети или таймаута ИИ. Ошибка: " + ex.Message + ". Всего нарядов: " + orders.Count;
        }
    }
}




