using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace Processor.Models;

[Table("document_chunks")]
public class DocumentChunk
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("content_vector", TypeName = "vector(3072)")]
    public Vector? ContentVector { get; set; }

    [Column("source_url")]
    [MaxLength(2048)]
    public string SourceUrl { get; set; } = string.Empty;

    [Column("chunk_index")]
    public int ChunkIndex { get; set; }

    [Column("clean_data_id")]
    public Guid CleanDataId { get; set; }
}
