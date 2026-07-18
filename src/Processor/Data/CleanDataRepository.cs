using Microsoft.EntityFrameworkCore;
using Processor.Models;

namespace Processor.Data;

public class CleanDataRepository
{
    private readonly AppDbContext _db;

    public CleanDataRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetNextVersionAsync(string url)
    {
        var latest = await _db.CleanedData
            .Where(c => c.Url == url)
            .OrderByDescending(c => c.Version)
            .Select(c => c.Version)
            .FirstOrDefaultAsync();

        return latest + 1;
    }

    public async Task InsertAsync(CleanedData data)
    {
        _db.CleanedData.Add(data);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CleanedData>> QueryAsync(string? domain = null, int? version = null)
    {
        var query = _db.CleanedData.AsQueryable();

        if (!string.IsNullOrEmpty(domain))
            query = query.Where(c => c.Url.Contains(domain));

        if (version.HasValue)
            query = query.Where(c => c.Version == version.Value);

        return await query
            .OrderByDescending(c => c.ProcessedAt)
            .Take(50)
            .ToListAsync();
    }
}
