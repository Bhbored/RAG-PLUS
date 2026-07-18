using System.Text.Json;

namespace Processor.Models;

public class ScrapedContent
{
    public string Title { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public List<string> Headings { get; set; } = new();
    public List<TableData> Tables { get; set; } = new();
    public List<string> Links { get; set; } = new();
    public DateTime? PublishDate { get; set; }
}

public class TableData
{
    public string Caption { get; set; } = string.Empty;
    public List<List<string>> Rows { get; set; } = new();
}
