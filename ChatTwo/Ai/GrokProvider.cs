using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatTwo.Ai;

/// <summary>
/// Provider for xAI's Grok, using the Responses API:
/// POST /v1/responses with { model, input: [ { role, content } ] }, replying
/// with { output: [ { content: [ { type: "output_text", text } ] } ] }.
/// Grok 4.x models are reasoning models, so the output array can contain
/// reasoning items before the message item.
/// </summary>
public class GrokProvider : IAiProvider
{
    private const string BaseUrl = "https://api.x.ai/v1";

    /// <summary> Text models to offer when the API can't be asked. </summary>
    public static readonly string[] KnownModels = ["grok-4.5", "grok-4.3"];

    /// <summary>
    /// Reasoning tokens are billed as output, and translation gains nothing
    /// from them, but only grok-4.3 lets us turn them down.
    /// </summary>
    public static bool SupportsReasoningEffort(string model) =>
        model.StartsWith("grok-4.3", StringComparison.OrdinalIgnoreCase);

    public async Task<AiResponse> ChatAsync(AiRequest request, CancellationToken token)
    {
        var apiKey = SecretUtil.Open(Plugin.Config.GrokApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Grok API key is not set");

        var model = Plugin.Config.GrokModel;

        // Stable parts first, the new text last: xAI only reuses a cached
        // prefix that matches exactly.
        var input = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt } };
        if (!string.IsNullOrWhiteSpace(request.Context))
            input.Add(new JsonObject { ["role"] = "user", ["content"] = request.Context });
        input.Add(new JsonObject { ["role"] = "user", ["content"] = request.UserText });

        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = input,
        };

        if (request.MaxOutputTokens > 0)
            body["max_output_tokens"] = request.MaxOutputTokens;

        if (SupportsReasoningEffort(model) && !string.IsNullOrWhiteSpace(Plugin.Config.GrokReasoningEffort))
            body["reasoning"] = new JsonObject { ["effort"] = Plugin.Config.GrokReasoningEffort };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);
        if (!string.IsNullOrWhiteSpace(request.ConversationId))
            httpRequest.Headers.TryAddWithoutValidation("x-grok-conv-id", request.ConversationId);

        httpRequest.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await AiUtil.HttpClient.SendAsync(httpRequest, token);
        var raw = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Grok returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

        var json = JsonNode.Parse(raw);
        var content = ExtractOutputText(json);
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonException($"Grok response had no content: {AiUtil.Truncate(raw)}");

        var usage = json?["usage"];
        return new AiResponse
        {
            Text = content.Trim(),
            InputTokens = usage?["input_tokens"]?.GetValue<int>() ?? 0,
            OutputTokens = usage?["output_tokens"]?.GetValue<int>() ?? 0,
            CachedTokens = usage?["input_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0,
            ReasoningTokens = usage?["output_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken token)
    {
        var apiKey = SecretUtil.Open(Plugin.Config.GrokApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Grok API key is not set");

        // Listing models isn't part of the documented API, so fall back to the
        // known text models instead of failing the whole request.
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.TryAddWithoutValidation("User-Agent", AiUtil.UserAgent);

            using var response = await AiUtil.HttpClient.SendAsync(request, token);
            var raw = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Grok returned {(int)response.StatusCode}: {AiUtil.Truncate(raw)}");

            var models = new List<string>();
            if (JsonNode.Parse(raw)?["data"] is JsonArray data)
                foreach (var entry in data)
                    if (entry?["id"]?.GetValue<string>() is { } id)
                        models.Add(id);

            if (models.Count == 0)
                return [..KnownModels];

            models.Sort();
            return models;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Plugin.Log.Warning(ex, "Listing Grok models failed, using the built-in list");
            return [..KnownModels];
        }
    }

    /// <summary>
    /// Collects the text of every "output_text" part in the response, skipping
    /// reasoning items. Falls back to the chat-completions shape in case the
    /// OpenAI-compatible endpoint is used instead.
    /// </summary>
    private static string? ExtractOutputText(JsonNode? root)
    {
        if (root?["output"] is JsonArray output)
        {
            var builder = new StringBuilder();
            foreach (var item in output)
            {
                if (item?["content"] is not JsonArray parts)
                    continue;

                foreach (var part in parts)
                    if (part?["type"]?.GetValue<string>() == "output_text" && part["text"]?.GetValue<string>() is { } text)
                        builder.Append(text);
            }

            if (builder.Length > 0)
                return builder.ToString();
        }

        return root?["output_text"]?.GetValue<string>()
               ?? root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
    }
}
