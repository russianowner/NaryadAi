using System.Text;
using System.Text.Json;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class AiAnalyticsService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    public async Task<string> GenerateShiftSummaryAsync(IReadOnlyList<WorkOrder> orders, CancellationToken cancellationToken = default)
    {
        var endpoint = configuration["Ai:AnthropicEndpoint"];
        var apiKey = configuration["Ai:ApiKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return "ИИ-аналитика недоступна (не настроен API ключ). За выбранный период обработано нарядов: " + orders.Count;
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(AiAnalyticsService));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

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
                model = configuration["Ai:Model"] ?? "claude-3-5-haiku-latest",
                max_tokens = 300,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = "Статистика смены:\n" + stats.ToString() } }
            }), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var text = document.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "Нет данных";
            return text;
        }
        catch
        {
            return "Не удалось сгенерировать сводку из-за ошибки сети или таймаута ИИ. Всего нарядов: " + orders.Count;
        }
    }
}
