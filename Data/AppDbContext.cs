using Microsoft.EntityFrameworkCore;
using PaperAggro.Models;

namespace PaperAggro.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<FeedSourceEntity> Feeds => Set<FeedSourceEntity>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Article>().HasIndex(a => a.ExternalId).IsUnique();
        mb.Entity<Article>().HasIndex(a => a.Category);
        mb.Entity<Article>().HasIndex(a => a.PublishedAt);
        mb.Entity<Subscriber>().HasIndex(s => s.Email).IsUnique();
    }
}