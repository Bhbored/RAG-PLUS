using System.Text;

namespace Processor.Services;

public class ChunkingService
{
    private const int ChunkSizeChars = 2000;  // ~500 tokens at 4 chars/token
    private const int OverlapChars = 200;     // ~50 tokens

    public List<ChunkResult> ChunkContent(string text, string url)
    {
        var chunks = new List<ChunkResult>();

        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        var paragraphs = text
            .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        var current = new StringBuilder();
        int chunkIndex = 0;

        foreach (var para in paragraphs)
        {
            if (current.Length + para.Length > ChunkSizeChars && current.Length > 0)
            {
                chunks.Add(new ChunkResult
                {
                    Text = current.ToString().Trim(),
                    SourceUrl = url,
                    ChunkIndex = chunkIndex++,
                });

                var overlap = GetOverlap(current.ToString(), OverlapChars);
                current.Clear();
                if (overlap.Length > 0)
                    current.AppendLine(overlap);
            }

            current.AppendLine(para);
        }

        // Final chunk
        if (current.Length > 0)
        {
            chunks.Add(new ChunkResult
            {
                Text = current.ToString().Trim(),
                SourceUrl = url,
                ChunkIndex = chunkIndex,
            });
        }

        return chunks;
    }

    private static string GetOverlap(string text, int overlapChars)
    {
        if (text.Length <= overlapChars)
            return text;

        // Find the start of the last paragraph within the overlap window
        var start = text.Length - overlapChars;
        var idx = text.IndexOf('\n', start);
        if (idx > 0)
            return text[idx..].Trim();

        return text[^overlapChars..].Trim();
    }
}

public class ChunkResult
{
    public string Text { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
}
