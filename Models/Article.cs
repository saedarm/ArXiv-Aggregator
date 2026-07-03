namespace PaperAggro.Models;

public class Article
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = "";   // dedupe key (the link)
    public string Title { get; set; } = "";
    public string Link { get; set; } = "";
    public string? PdfLink { get; set; }           // research assistant pipeline
    public string Description { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceType { get; set; } = "rss"; // rss | arxiv
    public string Category { get; set; } = "General AI";
    public string TagsCsv { get; set; } = "";
    public bool DeepDive { get; set; }
    public string? AiSummary { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime CollectedAt { get; set; }

    public string[] Tags =>
        TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

public class Subscriber
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime SubscribedAt { get; set; }
    public bool Active { get; set; } = true;
}

public class FeedSourceEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Type { get; set; } = "rss";   // rss | arxiv
    public bool Enabled { get; set; } = true;
    public DateTime? LastCollectedAt { get; set; }
    public int LastCollectedCount { get; set; }
    public string? LastError { get; set; }
}

public class AppSettings
{
    public int Id { get; set; } = 1;            // single row
    public int CollectHours { get; set; } = 4;
    public bool DailyDigestEnabled { get; set; } = true;
    public string DailyDigestTime { get; set; } = "07:30";
    public bool WeeklyDigestEnabled { get; set; } = true;
    public DayOfWeek WeeklyDigestDay { get; set; } = DayOfWeek.Monday;
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 465;
    public string SmtpUser { get; set; } = "";
    // password lives in SMTP_PASSWORD env var / user-secrets — never the DB
}