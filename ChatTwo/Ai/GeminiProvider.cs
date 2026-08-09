using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatTwo.Ai;

public class GeminiProvider : IAiProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public async Task<AiResponse> ChatAsync(AiRequest request, CancellationToken token)
    {
        var apiKey = SecretUtil.Open(Plugin.Config.GeminiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not set");

        static JsonObject UserPart(string text) => new()
        {
            ["role"] = "user",
            ["parts"] = new JsonArray { new JsonObject { ["text"] = text } },
        };

        var contents = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.Context))
            contents.Add(UserPart(request.Context));
        contents.Add(UserPart(request.UserText));

        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } },
            },
            ["contents"] = contents,
        };

        if (request.MaxOutputTokens > 0)
            body["generationConfig"] = new JsonObject { ["maxOutputTokens"] = request.MaxOutputTokens };

        var url = $"{BaseUrl}/models/{Plugin.Config.GeminiModel}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("x-goog-api-key", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);
        httpRequest.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await AiUtil.HttpClient.SendAsync(httpRequest, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        var json = JsonNode.Parse(raw);
        var content = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonException($"Gemini response had no content: {AiUtil.Truncate(raw)}");

        var usage = json?["usageMetadata"];
        return new AiResponse
        {
            Text = content.Trim(),
            InputTokens = usage?["promptTokenCount"]?.GetValue<int>() ?? 0,
            OutputTokens = usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0,
            CachedTokens = usage?["cachedContentTokenCount"]?.GetValue<int>() ?? 0,
            ReasoningTokens = usage?["thoughtsTokenCount"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken token)
    {
        var apiKey = SecretUtil.Open(Plugin.Config.GeminiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not set");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);

        using var response = await AiUtil.HttpClient.SendAsync(request, token);
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
