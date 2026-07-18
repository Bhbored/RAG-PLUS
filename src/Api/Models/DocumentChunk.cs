using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Models;

[Table("document_chunks")]
public class DocumentChunk
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    [Column("chunk_index")]
    public int ChunkIndex { get; set; }
}
