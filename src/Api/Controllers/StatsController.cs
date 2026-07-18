using Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly ApiDbContext _db;
    private readonly IConnectionMultiplexer _redis;

    public StatsController(ApiDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis;
    }

    [HttpGet]
    public async Task<IActionResult> GetStats()
    {
        var rawCount = await _db.RawScrapedData.CountAsync();
        var cleanedCount = await _db.CleanedData.CountAsync();
        var chunkCount = await _db.DocumentChunks.CountAsync();

        var uniqueDomains = await _db.RawScrapedData
            .Select(r => r.Domain)
            .Distinct()
            .CountAsync();

        long queueWaiting = 0;
        long queueActive = 0;
        long queueCompleted = 0;
        long dlqCount = 0;

        try
        {
            var db = _redis.GetDatabase();
            var waiting = await db.ListLengthAsync("bull:scrape-queue:waiting");
            var active = await db.ListLengthAsync("bull:scrape-queue:active");
            var completed = await db.ListLengthAsync("bull:scrape-queue:completed");
            var dlq = await db.ListLengthAsync("scrape-dead-letter");

            queueWaiting = waiting;
            queueActive = active;
            queueCompleted = completed;
            dlqCount = dlq;
        }
        catch
        {
            // Redis unavailable
        }

        return Ok(new
        {
            rawCount,
            cleanedCount,
            chunkCount,
            uniqueDomains,
            queue = new { waiting = queueWaiting, active = queueActive, completed = queueCompleted, deadLetter = dlqCount },
        });
    }
}
