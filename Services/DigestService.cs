using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using PaperAggro.Data;
using PaperAggro.Models;

namespace PaperAggro.Services;

public class DigestService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<DigestService> logger)
{
    public async Task SendDigestAsync(TimeSpan window, string label,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var since = DateTime.UtcNow - window;

        var articles = await db.Articles
            .Where(a => a.CollectedAt >= since)
            .OrderByDescending(a => a.DeepDive)
            .ThenByDescending(a => a.PublishedAt)
            .ToListAsync(ct);

        if (articles.Count == 0) return;

        var subscribers = await db.Subscribers
            .Where(s => s.Active)
            .Select(s => s.Email)
            .ToListAsync(ct);
        if (subscribers.Count == 0) return;

        var html = BuildHtml(articles, label);
        await SendAsync(settings, subscribers,
            $"PaperAggro {label} — {articles.Count} new items", html, ct);
    }

    public async Task ExportDeepDiveManifestAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = DateTime.UtcNow.AddDays(-7);

        var papers = await db.Articles
            .Where(a => a.DeepDive && a.PdfLink != null && a.CollectedAt >= since)
            .Select(a => new { a.Title, a.PdfLink, a.Link, a.TagsCsv, a.AiSummary })
            .ToListAsync(ct);

        if (papers.Count == 0) return;

        Directory.CreateDirectory("papers_for_deep_dive");
        var path = Path.Combine("papers_for_deep_dive",
            $"papers_{DateTime.UtcNow:yyyy-MM-dd}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new { exported = DateTime.UtcNow, papers },
            new JsonSerializerOptions { WriteIndented = true }), ct);

        logger.LogInformation("Exported {Count} deep-dive papers to {Path}",
            papers.Count, path);
    }

    private static string BuildHtml(List<Article> articles, string label)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:0 auto">
            <h1 style="color:#2c2c2a">PaperAggro {label} digest</h1>
            """);

        var deepDives = articles.Where(a => a.DeepDive).ToList();
        if (deepDives.Count > 0)
        {
            sb.Append("<h2>Recommended for deep dive</h2>");
            foreach (var a in deepDives)
            {
                sb.Append($"""
                    <div style="border-left:3px solid #534AB7;padding:8px 12px;margin:8px 0">
                    <a href="{a.Link}"><strong>{a.Title}</strong></a>
                    {(a.PdfLink is null ? "" : $""" &nbsp;<a href="{a.PdfLink}">[PDF]</a>""")}
                    <p style="color:#5f5e5a;font-size:14px">{a.AiSummary ?? a.Description}</p>
                    </div>
                    """);
            }
        }

        foreach (var group in articles.Where(a => !a.DeepDive)
                     .GroupBy(a => a.Category))
        {
            sb.Append($"<h2>{group.Key} ({group.Count()})</h2>");
            foreach (var a in group.Take(10))
                sb.Append($"""
                    <p><a href="{a.Link}">{a.Title}</a><br>
                    <span style="color:#888780;font-size:13px">
                    {a.SourceName} · {a.PublishedAt:MMM d}</span></p>
                    """);
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private async Task SendAsync(AppSettings settings, List<string> to,
        string subject, string html, CancellationToken ct)
    {
        var pass = Environment.GetEnvironmentVariable("SMTP_PASSWORD");

        if (string.IsNullOrEmpty(settings.SmtpHost)
            || string.IsNullOrEmpty(settings.SmtpUser)
            || string.IsNullOrEmpty(pass))
        {
            logger.LogInformation(
                "SMTP not configured — digest built but not sent");
            return;
        }

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(settings.SmtpUser));
        foreach (var addr in to) msg.Bcc.Add(MailboxAddress.Parse(addr));
        msg.Subject = subject;
        msg.Body = new TextPart("html") { Text = html };

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, true, ct);
        await client.AuthenticateAsync(settings.SmtpUser, pass, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}