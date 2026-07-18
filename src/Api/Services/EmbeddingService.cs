using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Services;

public class EmbeddingService
{
    private const string EmbeddingModel = "text-embedding-3-large";
    private const int EmbeddingDimensions = 3072;

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public EmbeddingService(HttpClient http)
    {
        _http = http;
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return new float[EmbeddingDimensions];

        var request = new { model = EmbeddingModel, input = text.Trim() };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
        httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
        httpRequest.Content = JsonContent.Create(request);

        var response = await _http.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var arr = json!.RootElement.GetProperty("data")[0].GetProperty("embedding");

        var result = new float[arr.GetArrayLength()];
        for (int i = 0; i < result.Length; i++)
            result[i] = arr[i].GetSingle();

        return result;
    }
}
