using System.Text;
using System.Text.Json;
using NaryadAi.Models;

namespace NaryadAi.Services;

public record AnomalyInsight(
    string Category, 
    string EquipmentName, 
    string Title, 
    string Description, 
    string Recommendation, 
    string Severity 
);

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

    public async Task<List<AnomalyInsight>> DetectAnomaliesAsync(IReadOnlyList<WorkOrder> orders, CancellationToken cancellationToken = default)
    {
        var insights = new List<AnomalyInsight>();
        var recurringFaults = orders
            .Where(x => x.Equipment != null && !string.IsNullOrWhiteSpace(x.CloseFaultCode))
            .GroupBy(x => new { EquipmentName = x.Equipment!.Name, x.CloseFaultCode })
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Take(4);

        foreach (var rf in recurringFaults)
        {
            insights.Add(new AnomalyInsight(
                Category: "Повторные отказы",
                EquipmentName: rf.Key.EquipmentName,
                Title: $"Повторные поломки по шифру {rf.Key.CloseFaultCode}",
                Description: $"Зафиксировано {rf.Count()} однотипных случаев поломки. Признак того, что стандартный текущий ремонт не устраняет коренную причину износа.",
                Recommendation: "Провести углублённую ревизию узла, проверить соосность/смазку и включить узел в план ближайшего ППР.",
                Severity: rf.Count() >= 4 ? "High" : "Medium"
            ));
        }
        var problemEquipment = orders
            .Where(x => x.Equipment != null)
            .GroupBy(x => x.Equipment!.Name)
            .Select(g => new { 
                Name = g.Key, 
                Total = g.Count(), 
                Emergency = g.Count(o => o.Priority == "Аварийный" || o.Type == "Внеплановый") 
            })
            .Where(x => x.Emergency >= 2)
            .OrderByDescending(x => x.Emergency)
            .Take(3);

        foreach (var pe in problemEquipment)
        {
            if (insights.Any(i => i.EquipmentName == pe.Name)) continue;

            insights.Add(new AnomalyInsight(
                Category: "Топ проблемных узлов",
                EquipmentName: pe.Name,
                Title: $"Повышенная аварийность: {pe.Emergency} внеплановых нарядов",
                Description: $"На данное оборудование приходится {pe.Emergency} из {pe.Total} зафиксированных ремонтов. Высокий риск внезапной остановки технологической линии.",
                Recommendation: "Установить учащённый вибродиагностический контроль и обеспечить запас критических быстроизнашиваемых деталей на складе.",
                Severity: "High"
            ));
        }

        var reworkOrders = orders
            .Where(x => x.Equipment != null && x.Status == WorkOrderStates.Rework)
            .GroupBy(x => x.Equipment!.Name)
            .Take(2);

        foreach (var rw in reworkOrders)
        {
            insights.Add(new AnomalyInsight(
                Category: "Качество выполнения",
                EquipmentName: rw.Key,
                Title: "Наряды возвращены на доработку после проверки ИИ",
                Description: $"{rw.Count()} наряд(ов) не прошли входной контроль ИИ или мастера (неполные работы, расхождения по фото или завышение времени).",
                Recommendation: "Провести инструктаж с бригадой исполнителей по регламенту закрытия нарядов и фотофиксации.",
                Severity: "Warning"
            ));
        }
        var groqKey = configuration["Ai:GroqApiKey"];
        if (!string.IsNullOrWhiteSpace(groqKey) && insights.Count > 0)
        {
            try
            {
                var client = httpClientFactory.CreateClient(nameof(AiAnalyticsService));
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {groqKey}");

                var promptContext = string.Join("\n", insights.Select(i => $"- {i.EquipmentName}: {i.Title} ({i.Category})"));
                var systemPrompt = "Ты — промышленный эксперт ТОиР горно-обогатительного комбината. На основе списка выявленных аномалий сформируй одну главную предиктивную рекомендацию для главного механика (2 предложения).";

                request.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = "qwen/qwen3.8-27b",
                    max_tokens = 200,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = promptContext }
                    }
                }), Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var document = JsonDocument.Parse(jsonResponse);
                    var aiAdvice = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    if (!string.IsNullOrWhiteSpace(aiAdvice))
                    {
                        insights.Insert(0, new AnomalyInsight(
                            Category: "Предиктивный прогноз ИИ",
                            EquipmentName: "Общий технологический контур",
                            Title: "Сводная рекомендация службы надёжности",
                            Description: aiAdvice.Trim(),
                            Recommendation: "Внести корректировки в график планово-предупредительных ремонтов (ППР) на следующую декаду.",
                            Severity: "High"
                        ));
                    }
                }
            }
            catch
            {
                
            }
        }

        return insights;
    }
}
