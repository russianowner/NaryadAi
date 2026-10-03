using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public record AiReviewResult(string Verdict, decimal Score, string Explanation);

public class AiReviewService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    AppDbContext db)
{
    public async Task<AiReviewResult> ReviewAsync(WorkOrder order, CancellationToken cancellationToken = default)
    {
        var endpoint = configuration["Ai:AnthropicEndpoint"];
        var apiKey = configuration["Ai:ApiKey"];
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                return await ReviewWithClaudeAsync(order, endpoint, apiKey, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                // Keep order processing available if the external provider is not reachable.
            }
        }

        return await ReviewWithRulesAsync(order, cancellationToken);
    }

    private async Task<AiReviewResult> ReviewWithClaudeAsync(
        WorkOrder order, string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(AiReviewService));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = configuration["Ai:Model"] ?? "claude-3-5-haiku-latest",
            max_tokens = 500,
            system = "Проверь наряд. Верни только JSON: {\"verdict\":\"Принято|Принято с замечаниями|Требует доработки\",\"score\":0-100,\"explanation\":\"краткое объяснение на русском\"}. Сверь описание и выполненные работы, оцени наличие шифра поломки, материалов и фото после.",
            messages = new[] { new { role = "user", content = $"Проблема: {order.Description}\nВыполнено: {order.CloseWorksDone}\nШифр: {order.CloseFaultCode}\nКомментарий: {order.CloseComment}\nСписания: {string.Join("; ", order.Materials.Select(x => $"{x.Material} {x.Quantity} {x.Unit}"))}\nФото после: {order.Photos.Any(p => p.Type == "После")}" } }
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var text = document.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException("Модель вернула ответ без JSON");
        using var result = JsonDocument.Parse(text[start..(end + 1)]);
        var root = result.RootElement;
        var verdict = root.GetProperty("verdict").GetString() ?? "Принято с замечаниями";
        var score = Math.Clamp(root.GetProperty("score").GetDecimal(), 0, 100);
        var explanation = root.GetProperty("explanation").GetString() ?? "Оценка сформирована ИИ.";
        return new AiReviewResult(verdict, score, explanation);
    }

    private async Task<AiReviewResult> ReviewWithRulesAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var photosExist = await db.WorkOrderPhotos.AnyAsync(
            p => p.WorkOrderId == order.Id && p.Type == "После", cancellationToken);
        var work = order.CloseWorksDone?.Trim() ?? string.Empty;
        var hasMaterials = await db.MaterialWriteOffs.AnyAsync(x => x.WorkOrderId == order.Id, cancellationToken);
        var descriptionWords = order.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim('.', ',', ':', ';', '!', '?').ToLowerInvariant())
            .Where(x => x.Length > 4).ToHashSet();
        var workWords = work.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim('.', ',', ':', ';', '!', '?').ToLowerInvariant()).ToHashSet();
        var overlap = descriptionWords.Count == 0 ? 1m : (decimal)descriptionWords.Intersect(workWords).Count() / descriptionWords.Count;
        var score = 45m + Math.Min(25m, work.Length / 12m) + (string.IsNullOrWhiteSpace(order.CloseFaultCode) ? 0 : 10)
            + (photosExist ? 10 : 0) + (hasMaterials || string.IsNullOrWhiteSpace(order.CloseComment) ? 0 : 5)
            + overlap * 10m;
        score = Math.Clamp(decimal.Round(score, 0), 0, 100);

        if (work.Length < 20 || (order.Type == "Внеплановый" && !photosExist))
            return new AiReviewResult("Требует доработки", score, "Недостаточно подробное описание работ или отсутствует обязательное фото после выполнения.");
        if (score < 75)
            return new AiReviewResult("Принято с замечаниями", score, "Заполните материалы и комментарий, если они использовались. Сверьте описание результата с проблемой.");
        return new AiReviewResult("Принято", score, "Текст выполненных работ согласуется с описанием. Проверьте результат перед закрытием наряда.");
    }
}
