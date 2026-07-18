using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Processor.Services;

public class EmbeddingService
{
    private const string EmbeddingModel = "text-embedding-3-large";
    private const int EmbeddingDimensions = 3072;

    private readonly HttpClient _http;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string _apiKey;

    public EmbeddingService(HttpClient http, ILogger<EmbeddingService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY not set. Returning zero vector.");
            return new float[EmbeddingDimensions];
        }

        try
        {
            var request = new
            {
                model = EmbeddingModel,
                input = text.Trim(),
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
            httpRequest.Content = JsonContent.Create(request);

            var response = await _http.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var embedding = json.GetProperty("data")[0].GetProperty("embedding");

            var result = new float[embedding.GetArrayLength()];
            for (int i = 0; i < result.Length; i++)
                result[i] = embedding[i].GetSingle();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding");
            throw;
        }
    }

    public async Task<Dictionary<string, float[]>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        var result = new Dictionary<string, float[]>();
        foreach (var text in texts)
        {
            result[text] = await GenerateEmbeddingAsync(text);
            // Small delay to respect rate limits
            await Task.Delay(100);
        }
        return result;
    }
}
