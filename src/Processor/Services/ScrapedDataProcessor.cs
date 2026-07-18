using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Processor.Data;
using Processor.Models;
using Processor.Services;
using Processor.Validation;
using StackExchange.Redis;

using Microsoft.EntityFrameworkCore;

namespace Processor;

public class ScrapedDataProcessor : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScrapedDataProcessor> _logger;

    public ScrapedDataProcessor(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        ILogger<ScrapedDataProcessor> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Catch up on backlog first
        await ProcessBacklogAsync(stoppingToken);

        // Then subscribe to real-time events
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal("raw-data-ready"),
            async (channel, message) =>
        {
            _logger.LogInformation("Received raw-data-ready");

            try
            {
                var notification = JsonSerializer.Deserialize<RawDataNotification>(
                    message.ToString());
                if (notification is not null && !string.IsNullOrEmpty(notification.Url))
                {
                    await ProcessAndIndexAsync(notification.Url, notification.Id, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process raw-data-ready message");
            }
        });

        _logger.LogInformation("ScrapedDataProcessor started. Listening for raw-data-ready...");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessBacklogAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var rawIds = await db.RawScrapedData
                .Where(r => !db.CleanedData.Any(c => c.Url == r.Url))
                .OrderBy(r => r.ScrapedAt)
                .Take(100)
                .Select(r => new { r.Id, r.Url })
                .ToListAsync(ct);

            if (rawIds.Count == 0) return;

            _logger.LogInformation("Processing backlog: {Count} unprocessed raw records", rawIds.Count);

            foreach (var raw in rawIds)
            {
                await ProcessAndIndexAsync(raw.Url, raw.Id.ToString(), ct);
            }

            _logger.LogInformation("Backlog processed: {Count} records", rawIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backlog processing skipped (table may not exist yet)");
        }
    }

    private async Task ProcessAndIndexAsync(
        string url,
        string rawId,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cleanRepo = scope.ServiceProvider.GetRequiredService<CleanDataRepository>();
        var cleaner = scope.ServiceProvider.GetRequiredService<HtmlCleaner>();
        var validator = scope.ServiceProvider.GetRequiredService<ScrapedContentValidator>();

        // Find most recent raw data for this URL (supports both ID-based and URL-based lookup)
        RawScrapedData? raw = null;

        if (!string.IsNullOrEmpty(rawId) && Guid.TryParse(rawId, out var id))
        {
            raw = await db.RawScrapedData.FindAsync(id, stoppingToken);
        }

        if (raw is null)
        {
            raw = await db.RawScrapedData
                .Where(r => r.Url == url)
                .OrderByDescending(r => r.ScrapedAt)
                .FirstOrDefaultAsync(stoppingToken);
        }

        if (raw is null)
        {
            _logger.LogWarning("Raw data not found for URL: {Url}", url);
            return;
        }

        // 2. Parse HTML and strip boilerplate
        var doc = new HtmlDocument();
        doc.LoadHtml(raw.RawHtml);
        var structured = cleaner.Extract(url, doc);

        // 3. Schema validation
        var result = await validator.ValidateAsync(structured, stoppingToken);
        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.ErrorMessage));
            _logger.LogWarning("Validation failed for {Url}: {Errors}", url, errors);
            return;
        }

        // 4. Versioning: append new version, don't overwrite
        var version = await cleanRepo.GetNextVersionAsync(url);
        var cleaned = new CleanedData
        {
            Url = url,
            Title = structured.Title,
            StructuredContent = JsonSerializer.Serialize(structured),
            Version = version,
            ProcessedAt = DateTime.UtcNow,
        };

        await cleanRepo.InsertAsync(cleaned);

        _logger.LogInformation(
            "Processed {Url} → version {Version} (title: {Title}, body: {BodyLen} chars, tables: {TableCount})",
            url, version, structured.Title, structured.BodyText.Length, structured.Tables.Count);

        // 5. Indexing happens in Phase 4 (pgvector)
    }
}

public record RawDataNotification(string Url, string Id);
