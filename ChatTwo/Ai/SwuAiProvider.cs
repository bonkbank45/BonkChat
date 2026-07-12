using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChatTwo.Http;

namespace ChatTwo.Ai;

/// <summary>
/// Provider for the SWU AI service (Srinakharinwirot University), implemented
/// after the official "API Services SWU AI" manual v1:
/// - POST /swu/api/service/get-all-models with { user_id }
/// - POST /swu/api/service/chat with { user_id, model, content }
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
        var content = AiUtil.ExtractTextFromUnknownJson(raw);
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonException($"SWU AI response had no recognizable content: {AiUtil.Truncate(raw)}");

        return content.Trim();
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken token)
    {
        EnsureConfigured();

        var body = new JsonObject { ["user_id"] = Plugin.Config.SwuAiUserId };
        var raw = await Post("/swu/api/service/get-all-models", body, token);

        // The manual does not document the response shape, so collect every
        // string that plausibly names a model from the returned JSON.
        var models = new List<string>();
        try
        {
            CollectStrings(JsonNode.Parse(raw), models);
        }
        catch (JsonException)
        {
            throw new JsonException($"SWU AI get-all-models returned invalid JSON: {AiUtil.Truncate(raw)}");
        }

        return models.Distinct().ToList();
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
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await ServerCore.HttpClient.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"SWU AI returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        return raw;
    }

    private static void CollectStrings(JsonNode? node, List<string> into)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str):
                into.Add(str);
                break;
            case JsonArray array:
                foreach (var entry in array)
                    CollectStrings(entry, into);
                break;
            case JsonObject obj:
                // Prefer obvious name fields; otherwise recurse into everything.
                foreach (var key in new[] { "model", "name", "id" })
                {
                    if (obj[key] is JsonValue named && named.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str))
                    {
                        into.Add(str);
                        return;
                    }
                }

                foreach (var (_, child) in obj)
                    CollectStrings(child, into);
                break;
        }
    }
}
