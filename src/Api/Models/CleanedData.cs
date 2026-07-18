using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Models;

[Table("cleaned_data")]
public class CleanedData
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("url")]
    public string Url { get; set; } = string.Empty;

    [Column("title")]
    public string? Title { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [Column("processed_at")]
    public DateTime ProcessedAt { get; set; }
}
