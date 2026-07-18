using Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    private readonly ApiDbContext _db;

    public DataController(ApiDbContext db)
    {
        _db = db;
    }

    [HttpGet("raw")]
    public async Task<IActionResult> GetRaw([FromQuery] string? url, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.RawScrapedData.AsQueryable();

        if (!string.IsNullOrEmpty(url))
            query = query.Where(r => r.Url.Contains(url));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.ScrapedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.Url,
                r.Domain,
                r.Title,
                r.HttpStatus,
                r.RenderJs,
                r.WorkerId,
                r.ScrapedAt,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("cleaned")]
    public async Task<IActionResult> GetCleaned([FromQuery] string? domain, [FromQuery] int? version, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.CleanedData.AsQueryable();

        if (!string.IsNullOrEmpty(domain))
            query = query.Where(c => c.Url.Contains(domain));

        if (version.HasValue)
            query = query.Where(c => c.Version == version.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.ProcessedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.Url, c.Title, c.Version, c.ProcessedAt })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }
}
