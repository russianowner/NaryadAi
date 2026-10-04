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
    AppDbContext db,
    IWebHostEnvironment environment)
{
    public async Task<AiReviewResult> ReviewAsync(WorkOrder order, CancellationToken cancellationToken = default)
    {
        var groqKey = configuration["Ai:GroqApiKey"];
        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            try { return await ReviewWithGroqVisionAsync(order, groqKey, cancellationToken); }
            catch { /* Fallback */ }
        }

        var endpoint = configuration["Ai:AnthropicEndpoint"];
        var apiKey = configuration["Ai:ApiKey"];
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            try { return await ReviewWithClaudeAsync(order, endpoint, apiKey, cancellationToken); }
            catch { }
        }

        return await ReviewWithRulesAsync(order, cancellationToken);
    }

    private async Task<AiReviewResult> ReviewWithGroqVisionAsync(WorkOrder order, string apiKey, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(AiReviewService));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        var actualTimeStr = (order.StartedAt.HasValue && order.CompletedAt.HasValue) 
            ? $"{(order.CompletedAt.Value - order.StartedAt.Value).TotalMinutes:F0} минут" 
            : "неизвестно";
        var allowedTimeStr = (order.StartedAt.HasValue) 
            ? $"{(order.Deadline - order.StartedAt.Value).TotalMinutes:F0} минут" 
            : "неизвестно";

        var systemPrompt = @"Ты — строгий AI-контролер на промышленном предприятии.
Твоя задача — проверить закрытый наряд на выполнение и визуальное качество (по фото), затем вернуть JSON-ответ:
{
  ""verdict"": ""Принято"" | ""Принято с замечаниями"" | ""Требует доработки"",
  ""score"": <число от 0 до 100>,
  ""explanation"": ""краткое объяснение на русском""
}
Обязательно учитывай фотографии (до и после) если они переданы. Устранена ли проблема? Есть ли мусор? 
Проверяй также время выполнения и соответствие материалов шифру.";

        var textContent = $@"Проблема: {order.Description}
Выполнено: {order.CloseWorksDone}
Шифр поломки: {order.CloseFaultCode}
Материалы: {string.Join("; ", order.Materials.Select(x => $"{x.Material} {x.Quantity} {x.Unit}"))}
Время работы: {actualTimeStr} (разрешено: {allowedTimeStr})";

        var contentList = new List<object> { new { type = "text", text = textContent } };

        foreach (var p in order.Photos)
        {
            var path = Path.Combine(environment.ContentRootPath, p.FilePath);
            if (File.Exists(path))
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var base64 = Convert.ToBase64String(bytes);
                var mime = path.EndsWith(".png") ? "image/png" : path.EndsWith(".webp") ? "image/webp" : "image/jpeg";
                contentList.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{mime};base64,{base64}" }
                });
            }
        }

        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = "llama-3.2-11b-vision-preview",
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = contentList }
            }
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(jsonResponse);
        
        var text = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        using var result = JsonDocument.Parse(text);
        var root = result.RootElement;
        var verdict = root.GetProperty("verdict").GetString() ?? "Принято с замечаниями";
        var score = Math.Clamp(root.GetProperty("score").GetDecimal(), 0, 100);
        var explanation = root.GetProperty("explanation").GetString() ?? "Оценка Groq Vision.";
        return new AiReviewResult(verdict, score, explanation);
    }

    private async Task<AiReviewResult> ReviewWithClaudeAsync(WorkOrder order, string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(AiReviewService));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        var actualTimeStr = (order.StartedAt.HasValue && order.CompletedAt.HasValue) ? $"{(order.CompletedAt.Value - order.StartedAt.Value).TotalMinutes:F0} минут" : "неизвестно";
        var allowedTimeStr = order.StartedAt.HasValue ? $"{(order.Deadline - order.StartedAt.Value).TotalMinutes:F0} минут" : "неизвестно";
        var systemPrompt = @"Ты — строгий AI-контролер.
Верни JSON: { ""verdict"": ""Принято|Принято с замечаниями|Требует доработки"", ""score"": 0-100, ""explanation"": ""..."" }
Проверь наряд: работы, логичность материалов, время, наличие фото.";
        var userMessage = $@"Проблема: {order.Description}\nТип: {order.Type}\nВыполнено: {order.CloseWorksDone}\nШифр: {order.CloseFaultCode}\nМатериалы: {string.Join("; ", order.Materials.Select(x => $"{x.Material} {x.Quantity} {x.Unit}"))}\nФото после: {(order.Photos.Any(p => p.Type == "После") ? "есть" : "нет")}\nФактическое время: {actualTimeStr} (разрешено: {allowedTimeStr})";
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = configuration["Ai:Model"] ?? "claude-3-5-haiku-latest", max_tokens = 500, system = systemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
        }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
        var start = text.IndexOf('{'); var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException("No JSON returned");
        using var result = JsonDocument.Parse(text[start..(end + 1)]);
        var root = result.RootElement;
        return new AiReviewResult(root.GetProperty("verdict").GetString() ?? "Принято", Math.Clamp(root.GetProperty("score").GetDecimal(), 0, 100), root.GetProperty("explanation").GetString() ?? "");
    }

    private async Task<AiReviewResult> ReviewWithRulesAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var photosExist = await db.WorkOrderPhotos.AnyAsync(p => p.WorkOrderId == order.Id && p.Type == "После", cancellationToken);
        var work = order.CloseWorksDone?.Trim() ?? string.Empty;
        var hasMaterials = await db.MaterialWriteOffs.AnyAsync(x => x.WorkOrderId == order.Id, cancellationToken);
        var descriptionWords = order.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim('.', ',', ':', ';', '!', '?').ToLowerInvariant()).Where(x => x.Length > 4).ToHashSet();
        var workWords = work.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim('.', ',', ':', ';', '!', '?').ToLowerInvariant()).ToHashSet();
        var overlap = descriptionWords.Count == 0 ? 1m : (decimal)descriptionWords.Intersect(workWords).Count() / descriptionWords.Count;
        var score = 45m + Math.Min(25m, work.Length / 12m) + (string.IsNullOrWhiteSpace(order.CloseFaultCode) ? 0 : 10) + (photosExist ? 10 : 0) + (hasMaterials || string.IsNullOrWhiteSpace(order.CloseComment) ? 0 : 5) + overlap * 10m;
        score = Math.Clamp(decimal.Round(score, 0), 0, 100);
        if (work.Length < 20 || (order.Type == "Внеплановый" && !photosExist)) return new AiReviewResult("Требует доработки", score, "Недостаточно подробное описание работ или отсутствует фото.");
        if (score < 75) return new AiReviewResult("Принято с замечаниями", score, "Заполните материалы, сверьте результаты.");
        return new AiReviewResult("Принято", score, "Всё отлично.");
    }
}
