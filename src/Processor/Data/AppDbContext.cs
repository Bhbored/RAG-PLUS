using Microsoft.EntityFrameworkCore;
using Processor.Models;

namespace Processor.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RawScrapedData> RawScrapedData => Set<RawScrapedData>();
    public DbSet<CleanedData> CleanedData => Set<CleanedData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawScrapedData>(entity =>
        {
            entity.HasIndex(e => e.Url);
            entity.HasIndex(e => e.Domain);
            entity.HasIndex(e => e.ContentHash).IsUnique();
        });

        modelBuilder.Entity<CleanedData>(entity =>
        {
            entity.HasIndex(e => e.Url);
            entity.HasIndex(e => new { e.Url, e.Version }).IsUnique();
        });
    }
}
