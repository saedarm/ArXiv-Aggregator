namespace PaperAggro.Services;

public class CategoryService
{
    private static readonly Dictionary<string, string[]> Categories = new()
    {
        ["OpenAI"] = ["openai", "gpt-4", "gpt-5", "chatgpt", "sora", "o1", "o3"],
        ["Anthropic / Claude"] = ["anthropic", "claude", "sonnet", "opus", "haiku"],
        ["Google / Gemini"] = ["google", "gemini", "deepmind", "veo", "vertex"],
        ["RAG & Retrieval"] = ["rag", "retrieval", "retrieval-augmented",
            "vector", "embedding", "reranker"],
        ["Agents & Agentic"] = ["agent", "agentic", "tool use", "autonomous",
            "multi-agent", "mcp"],
        ["Research Papers"] = ["arxiv", "paper", "benchmark", "dataset"],
    };

    private static readonly string[] DeepDiveSignals =
    [
        "novel", "state-of-the-art", "sota", "outperform",
        "breakthrough", "we introduce", "we propose"
    ];

    public (string Category, string Tags, bool DeepDive) Classify(
        string title, string description, string sourceType)
    {
        var text = (title + " " + description).ToLowerInvariant();

        var category = "General AI";
        var tags = new List<string>();

        foreach (var (cat, keywords) in Categories)
        {
            var hits = keywords.Where(text.Contains).ToArray();
            if (hits.Length == 0) continue;
            if (category == "General AI") category = cat;
            tags.AddRange(hits.Take(2));
        }

        if (sourceType == "arxiv") category = "Research Papers";

        var deepDive = sourceType == "arxiv"
                       && DeepDiveSignals.Any(text.Contains);

        return (category, string.Join(',', tags.Distinct().Take(5)), deepDive);
    }
}