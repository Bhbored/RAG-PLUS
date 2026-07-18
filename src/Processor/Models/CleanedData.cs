using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Processor.Models;

[Table("cleaned_data")]
public class CleanedData
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();
    [Column("url")]
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;
    [Column("title")]
    [MaxLength(512)]
    public string Title { get; set; } = string.Empty;
    [Column("structured_content", TypeName = "jsonb")]
    public string StructuredContent { get; set; } = "{}";
    [Column("version")]
    public int Version { get; set; } = 1;
    [Column("processed_at")]
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
