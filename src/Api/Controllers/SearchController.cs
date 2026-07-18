using Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ApiDbContext _db;

    public SearchController(ApiDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string type = "keyword")
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required" });

        q = q.Trim();

        List<object> results;

        if (type == "keyword")
        {
            var chunks = await _db.DocumentChunks
                .Where(c => EF.Functions.ILike(c.Content, $"%{q}%"))
                .OrderBy(c => c.Content.Length)
                .Take(20)
                .Select(c => new { c.Id, c.SourceUrl, c.ChunkIndex, c.Content })
                .AsNoTracking()
                .ToListAsync();

            results = chunks
                .Select(c => (object)new
                {
                    c.Id,
                    c.SourceUrl,
                    c.ChunkIndex,
                    Snippet = HighlightSnippet(c.Content, q),
                })
                .ToList();
        }
        else
        {
            results = new List<object>
            {
                new { message = "Semantic search requires vector search. Use keyword search for now." }
            };
        }

        return Ok(new { query = q, type, results });
    }

    private static string HighlightSnippet(string content, string query)
    {
        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = 0;

        var start = Math.Max(0, idx - 80);
        var length = Math.Min(300, content.Length - start);
        var snippet = content.Substring(start, length).Replace("\n", " ");

        return start > 0 ? "..." + snippet + "..." : snippet + "...";
    }
}
