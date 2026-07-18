namespace Processor;

public class ProcessorOptions
{
    public int BatchSize { get; set; } = 10;
    public string ConnectionString { get; set; } = string.Empty;
}
