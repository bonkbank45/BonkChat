using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatTwo.Ai;

/// <summary>
/// Provider for the SWU AI service (Srinakharinwirot University), implemented
/// after the official "API Services SWU AI" manual v1:
/// - POST /swu/api/service/get-all-models with { user_id }
///   returns { "models": [ { "name": "..." } ], "count": n, "requested_by": ... }
/// - POST /swu/api/service/chat with { user_id, model, content }
///   returns an OpenAI-compatible completion (choices[0].message.content)
/// Both authenticated with "Authorization: Bearer &lt;SWU API KEY&gt;".
/// </summary>
public class SwuAiProvider : IAiProvider
{
    private const string BaseUrl = "https://swuai.swu.ac.th";

    public async Task<string> ChatAsync(string systemPrompt, string userText, CancellationToken token)
    {
        EnsureConfigured();

        // The documented API has no separate system prompt field, so the
        // instruction is inlined with the user's message.
        var body = new JsonObject
        {
            ["user_id"] = Plugin.Config.SwuAiUserId,
            ["model"] = Plugin.Config.SwuAiModel,
            ["content"] = $"{systemPrompt}\n\n{userText}",
        };

        var raw = await Post("/swu/api/service/chat", body, token);

        var json = JsonNode.Parse(raw);
        var content = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonException($"SWU AI response had no content: {AiUtil.Truncate(raw)}");

        return content.Trim();
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken token)
    {
        EnsureConfigured();

        var body = new JsonObject { ["user_id"] = Plugin.Config.SwuAiUserId };
        var raw = await Post("/swu/api/service/get-all-models", body, token);

        var json = JsonNode.Parse(raw);
        if (json?["models"] is not JsonArray modelArray)
            throw new JsonException($"SWU AI get-all-models had no model list: {AiUtil.Truncate(raw)}");

        var models = new List<string>();
        foreach (var entry in modelArray)
            if (entry?["name"]?.GetValue<string>() is { } name && !string.IsNullOrWhiteSpace(name))
                models.Add(name);

        models.Sort();
        return models;
    }

    private static void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Plugin.Config.SwuAiApiKey))
            throw new InvalidOperationException("SWU AI API key is not set");
        if (string.IsNullOrWhiteSpace(Plugin.Config.SwuAiUserId))
            throw new InvalidOperationException("SWU AI user ID is not set");
    }

    private static async Task<string> Post(string endpoint, JsonObject body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Plugin.Config.SwuAiApiKey);
        // Cloudflare in front of swuai.swu.ac.th rejects requests without a
        // User-Agent with a 403 challenge page, so identify ourselves.
        request.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await AiUtil.HttpClient.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"SWU AI returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        return raw;
    }
}
