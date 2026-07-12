using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChatTwo.Http;

namespace ChatTwo.Ai;

public class GeminiProvider : IAiProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public async Task<string> ChatAsync(string systemPrompt, string userText, CancellationToken token)
    {
        var apiKey = Plugin.Config.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not set");

        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } },
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = userText } },
                },
            },
        };

        var url = $"{BaseUrl}/models/{Plugin.Config.GeminiModel}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await ServerCore.HttpClient.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        var json = JsonNode.Parse(raw);
        var content = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonException($"Gemini response had no content: {AiUtil.Truncate(raw)}");

        return content.Trim();
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken token)
    {
        var apiKey = Plugin.Config.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not set");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await ServerCore.HttpClient.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        var json = JsonNode.Parse(raw);
        var models = new List<string>();
        if (json?["models"] is JsonArray data)
            foreach (var entry in data)
                if (entry?["name"]?.GetValue<string>() is { } name)
                    models.Add(name.StartsWith("models/") ? name["models/".Length..] : name);

        models.Sort();
        return models;
    }
}
