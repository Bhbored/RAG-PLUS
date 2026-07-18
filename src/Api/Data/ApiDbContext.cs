using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Data;

public class ApiDbContext : DbContext
{
    public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options) { }

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<RawScrapedData> RawScrapedData => Set<RawScrapedData>();
    public DbSet<CleanedData> CleanedData => Set<CleanedData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("document_chunks");
            entity.HasIndex(e => e.SourceUrl);
        });

        modelBuilder.Entity<RawScrapedData>(entity =>
        {
            entity.ToTable("raw_scraped_data");
            entity.HasIndex(e => e.Url);
            entity.HasIndex(e => e.Domain);
        });

        modelBuilder.Entity<CleanedData>(entity =>
        {
            entity.ToTable("cleaned_data");
            entity.HasIndex(e => e.Url);
        });
    }
}
