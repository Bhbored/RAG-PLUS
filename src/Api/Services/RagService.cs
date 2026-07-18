using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Services;

public class RagService
{
    private const int TopK = 8;

    private readonly ApiDbContext _db;
    private readonly EmbeddingService _embedder;
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public RagService(ApiDbContext db, EmbeddingService embedder, HttpClient http)
    {
        _db = db;
        _embedder = embedder;
        _http = http;
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    }

    public async Task<RagResponse> AskAsync(string question)
    {
        // 1. Generate question embedding
        var questionEmbedding = await _embedder.GenerateEmbeddingAsync(question);

        // 2. Vector similarity search via pgvector
        // Use raw SQL for cosine similarity since we need vector operations
        var vectorStr = $"[{string.Join(",", questionEmbedding.Select(f => f.ToString("F6")))}]";

        var chunks = await _db.DocumentChunks
            .FromSqlRaw("""
                SELECT id, content, source_url, chunk_index
                FROM document_chunks
                ORDER BY content_vector <=> {0}::vector
                LIMIT 8
                """,
                vectorStr)
            .AsNoTracking()
            .ToListAsync();

        if (chunks.Count == 0)
        {
            return new RagResponse
            {
                Answer = "No relevant documents found to answer your question.",
                Citations = new List<Citation>(),
            };
        }

        // 3. Build context with source citations
        var contextBuilder = new StringBuilder();
        var sources = new Dictionary<string, string>();

        foreach (var chunk in chunks)
        {
            contextBuilder.AppendLine($"[Source: {chunk.SourceUrl}]");
            contextBuilder.AppendLine(chunk.Content);
            contextBuilder.AppendLine();

            if (!sources.ContainsKey(chunk.SourceUrl))
                sources[chunk.SourceUrl] = chunk.SourceUrl;
        }

        // 4. Prompt engineering for synthesis + citations
        var prompt = $"""
You are a research assistant. Answer the question using ONLY the provided context.
If the answer requires combining information from multiple sources, synthesize it clearly.
Cite every fact using [Source: URL].

Context:
{contextBuilder}

Question: {question}

Answer:
""";

        // 5. Call GPT-4o
        var answer = await CallChatCompletionAsync(prompt);

        // 6. Build citations list
        var citations = chunks
            .GroupBy(c => c.SourceUrl)
            .Select(g => new Citation
            {
                Url = g.Key,
                Title = FormatTitle(g.Key),
                Excerpt = g.First().Content[..Math.Min(150, g.First().Content.Length)] + "...",
            })
            .DistinctBy(c => c.Url)
            .ToList();

        return new RagResponse
        {
            Answer = answer,
            Citations = citations,
        };
    }

    private async Task<string> CallChatCompletionAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "OPENAI_API_KEY not configured. Set it in the .env file.";

        try
        {
            var request = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 800,
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
            httpRequest.Content = JsonContent.Create(request);

            var response = await _http.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "No answer generated.";
        }
        catch (Exception ex)
        {
            return $"Error calling GPT-4o: {ex.Message}";
        }
    }

    private static string FormatTitle(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host.Replace("www.", "") + uri.AbsolutePath[..Math.Min(30, uri.AbsolutePath.Length)];
        }
        catch
        {
            return url;
        }
    }
}

public class RagResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new();
}

public class Citation
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
}
