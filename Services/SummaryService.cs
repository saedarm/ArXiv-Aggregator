using System.Text.Json;

namespace PaperAggro.Services;

public class SummaryService(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<SummaryService> logger)
{
    public async Task<string?> SummarizeAsync(
        string title, string abstractText, CancellationToken ct = default)
    {
        var apiKey = config["OpenAI:ApiKey"]
                     ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) return null; // fully optional

        try
        {
            var http = httpFactory.CreateClient("feeds");
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.openai.com/v1/chat/completions");
            req.Headers.Authorization = new("Bearer", apiKey);
            req.Content = JsonContent.Create(new
            {
                model = "gpt-4o-mini",
                max_tokens = 200,
                temperature = 0.3,
                messages = new[]
                {
                    new { role = "user", content =
                        $"Summarize this paper in 2 short bullet points:\n\n" +
                        $"Title: {title}\nAbstract: {abstractText}" }
                }
            });

            using var res = await http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(
                await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content").GetString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Summarization skipped");
            return null;
        }
    }
}