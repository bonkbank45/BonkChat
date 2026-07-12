using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatTwo.Ai;

public class OpenAiProvider : IAiProvider
{
    private const string BaseUrl = "https://api.openai.com/v1";

    public async Task<string> ChatAsync(string systemPrompt, string userText, CancellationToken token)
    {
        var apiKey = SecretUtil.Open(Plugin.Config.OpenAiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is not set");

        var body = new JsonObject
        {
            ["model"] = Plugin.Config.OpenAiModel,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userText },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await AiUtil.HttpClient.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        var json = JsonNode.Parse(raw);
        var content = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonException($"OpenAI response had no content: {AiUtil.Truncate(raw)}");

        return content.Trim();
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken token)
    {
        var apiKey = SecretUtil.Open(Plugin.Config.OpenAiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is not set");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);

        using var response = await AiUtil.HttpClient.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        var json = JsonNode.Parse(raw);
        var models = new List<string>();
        if (json?["data"] is JsonArray data)
            foreach (var entry in data)
                if (entry?["id"]?.GetValue<string>() is { } id)
                    models.Add(id);

        models.Sort();
        return models;
    }
}
