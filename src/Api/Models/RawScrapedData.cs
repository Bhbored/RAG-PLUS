using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Models;

[Table("raw_scraped_data")]
public class RawScrapedData
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("url")]
    public string Url { get; set; } = string.Empty;

    [Column("domain")]
    public string Domain { get; set; } = string.Empty;

    [Column("title")]
    public string? Title { get; set; }

    [Column("content_hash")]
    public string ContentHash { get; set; } = string.Empty;

    [Column("http_status")]
    public int HttpStatus { get; set; }

    [Column("render_js")]
    public bool RenderJs { get; set; }

    [Column("worker_id")]
    public string? WorkerId { get; set; }

    [Column("scraped_at")]
    public DateTime ScrapedAt { get; set; }
}
