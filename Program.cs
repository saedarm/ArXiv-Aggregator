using Microsoft.EntityFrameworkCore;
using PaperAggro.Components;
using PaperAggro.Data;
using PaperAggro.Models;
using PaperAggro.Services;
DotNetEnv.Env.TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=paperaggro.db"));

builder.Services.AddHttpClient("feeds", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("PaperAggro/2.0 (+personal digest)");
});

builder.Services.AddSingleton<CategoryService>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddSingleton<CollectorService>();
builder.Services.AddSingleton<DigestService>();
builder.Services.AddHostedService<SchedulerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();

    if (!db.Settings.Any())
        db.Settings.Add(new AppSettings());

    if (!db.Feeds.Any())
    {
        db.Feeds.AddRange(
            new FeedSourceEntity { Name = "OpenAI Blog", Url = "https://openai.com/blog/rss.xml" },
            new FeedSourceEntity { Name = "Anthropic News", Url = "https://www.anthropic.com/rss.xml" },
            new FeedSourceEntity { Name = "Google AI Blog", Url = "https://blog.google/technology/ai/rss/" },
            new FeedSourceEntity { Name = "Hugging Face", Url = "https://huggingface.co/blog/feed.xml" },
            new FeedSourceEntity { Name = "arXiv cs.AI", Type = "arxiv",
                Url = "https://export.arxiv.org/api/query?search_query=cat:cs.AI&sortBy=submittedDate&sortOrder=descending&max_results=15" },
            new FeedSourceEntity { Name = "arXiv cs.CL", Type = "arxiv",
                Url = "https://export.arxiv.org/api/query?search_query=cat:cs.CL&sortBy=submittedDate&sortOrder=descending&max_results=15" });
    }
    db.SaveChanges();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();