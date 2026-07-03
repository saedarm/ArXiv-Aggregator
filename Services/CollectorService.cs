using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using PaperAggro.Data;
using PaperAggro.Models;

namespace PaperAggro.Services;

public class CollectorService(
    IHttpClientFactory httpFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    CategoryService categorizer,
    SummaryService summarizer,
    ILogger<CollectorService> logger)
{
    public async Task<int> CollectAsync(CancellationToken ct = default)
    {
        List<FeedSourceEntity> sources;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            sources = await db.Feeds
                .AsNoTracking()
                .Where(f => f.Enabled)
                .ToListAsync(ct);
        }

        var results = await Task.WhenAll(
            sources.Select(s => CollectSourceAsync(s, ct)));
        return results.Sum();
    }

    private async Task<int> CollectSourceAsync(
        FeedSourceEntity source, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Feeds.FindAsync([source.Id], ct);

        try
        {
            var http = httpFactory.CreateClient("feeds");
            await using var stream = await http.GetStreamAsync(source.Url, ct);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);

            var added = 0;

            foreach (var item in feed.Items.Take(25))
            {
                var link = item.Links.FirstOrDefault(l =>
                        l.RelationshipType is null or "alternate")
                    ?.Uri.ToString() ?? "";
                if (string.IsNullOrEmpty(link)) continue;

                if (await db.Articles.AnyAsync(a => a.ExternalId == link, ct))
                    continue;

                var title = item.Title?.Text.Trim() ?? "(untitled)";
                var desc = item.Summary?.Text.Trim() ?? "";
                var pdf = FindPdfLink(item, source.Type);

                var (category, tags, deepDive) =
                    categorizer.Classify(title, desc, source.Type);

                string? aiSummary = null;
                if (deepDive)
                    aiSummary = await summarizer.SummarizeAsync(title, desc, ct);

                db.Articles.Add(new Article
                {
                    ExternalId = link,
                    Title = title,
                    Link = link,
                    PdfLink = pdf,
                    Description = desc.Length > 600 ? desc[..600] + "…" : desc,
                    SourceName = source.Name,
                    SourceType = source.Type,
                    Category = category,
                    TagsCsv = tags,
                    DeepDive = deepDive,
                    AiSummary = aiSummary,
                    PublishedAt = item.PublishDate.UtcDateTime,
                    CollectedAt = DateTime.UtcNow
                });
                added++;
            }

            if (tracked is not null)
            {
                tracked.LastCollectedAt = DateTime.UtcNow;
                tracked.LastCollectedCount = added;
                tracked.LastError = null;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("{Source}: {Count} new articles",
                source.Name, added);
            return added;
        }
        catch (Exception ex)
        {
            if (tracked is not null)
            {
                tracked.LastError = ex.Message;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            logger.LogWarning(ex, "Failed to collect from {Source}", source.Name);
            return 0;
        }
    }

    private static string? FindPdfLink(SyndicationItem item, string sourceType)
    {
        var explicitPdf = item.Links.FirstOrDefault(l =>
            l.MediaType == "application/pdf" ||
            l.Uri.ToString().Contains("/pdf/"))?.Uri.ToString();
        if (explicitPdf is not null) return explicitPdf;

        if (sourceType == "arxiv")
        {
            var abs = item.Links.FirstOrDefault()?.Uri.ToString();
            if (abs?.Contains("/abs/") == true)
                return abs.Replace("/abs/", "/pdf/");
        }
        return null;
    }
}