using Microsoft.EntityFrameworkCore;
using Processor.Models;

namespace Processor.Data;

public class RawDataRepository
{
    private readonly AppDbContext _db;

    public RawDataRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RawScrapedData?> GetByIdAsync(Guid id)
    {
        return await _db.RawScrapedData.FindAsync(id);
    }

    public async Task<List<RawScrapedData>> GetByUrlAsync(string url, int page = 1, int pageSize = 20)
    {
        return await _db.RawScrapedData
            .Where(r => r.Url.Contains(url))
            .OrderByDescending(r => r.ScrapedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<RawScrapedData>> GetUnprocessedAsync(int limit = 10)
    {
        return await _db.RawScrapedData
            .OrderBy(r => r.ScrapedAt)
            .Take(limit)
            .ToListAsync();
    }
}
