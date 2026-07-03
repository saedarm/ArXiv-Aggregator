using Microsoft.EntityFrameworkCore;
using PaperAggro.Data;

namespace PaperAggro.Services;

public class SchedulerService(
    CollectorService collector,
    DigestService digest,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<SchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastCollect = DateTime.MinValue;
        DateOnly lastDaily = default, lastWeekly = default;

        await SafeCollect(ct);
        lastCollect = DateTime.UtcNow;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var s = await db.Settings.AsNoTracking().FirstAsync(ct);

            var digestTime = TimeOnly.TryParse(s.DailyDigestTime, out var t)
                ? t : new TimeOnly(7, 30);

            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);

            if (DateTime.UtcNow - lastCollect >= TimeSpan.FromHours(s.CollectHours))
            {
                await SafeCollect(ct);
                lastCollect = DateTime.UtcNow;
            }

            if (s.DailyDigestEnabled && today != lastDaily
                && TimeOnly.FromDateTime(now) >= digestTime)
            {
                await digest.SendDigestAsync(TimeSpan.FromHours(24), "daily", ct);
                lastDaily = today;
            }

            if (s.WeeklyDigestEnabled && now.DayOfWeek == s.WeeklyDigestDay
                && today != lastWeekly && TimeOnly.FromDateTime(now) >= digestTime)
            {
                await digest.SendDigestAsync(TimeSpan.FromDays(7), "weekly", ct);
                await digest.ExportDeepDiveManifestAsync(ct);
                lastWeekly = today;
            }
        }
    }

    private async Task SafeCollect(CancellationToken ct)
    {
        try
        {
            var added = await collector.CollectAsync(ct);
            logger.LogInformation("Collection run: {Count} new articles", added);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collection run failed");
        }
    }
}