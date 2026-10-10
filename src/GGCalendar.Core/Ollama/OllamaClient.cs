using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GGCalendar.Core.Ollama;

public sealed class OllamaClient(string baseUrl, HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Returns the names of the models installed in the local Ollama instance.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var uri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/tags");
        var tags = await _http.GetFromJsonAsync<TagsResponse>(uri, ct).ConfigureAwait(false);
        return tags?.Models?.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).Order().ToList() ?? [];
    }

    private sealed record TagsResponse([property: JsonPropertyName("models")] List<ModelEntry>? Models);
    private sealed record ModelEntry([property: JsonPropertyName("name")] string Name);
}
