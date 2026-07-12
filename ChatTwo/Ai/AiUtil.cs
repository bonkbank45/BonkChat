using System.Text.Json.Nodes;

namespace ChatTwo.Ai;

public static class AiUtil
{
    public static string Truncate(string text, int max = 300)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    /// <summary>
    /// Best-effort extraction of the reply text from a JSON response whose
    /// shape is not documented. Checks well-known field names at any depth,
    /// otherwise falls back to the raw body if it isn't JSON at all.
    /// </summary>
    public static string? ExtractTextFromUnknownJson(string raw)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch (Exception)
        {
            // Not JSON; assume the body itself is the reply.
            return raw;
        }

        // OpenAI-compatible shape first, since many gateways proxy it.
        if (root?["choices"]?[0]?["message"]?["content"] is JsonValue openAi
            && openAi.TryGetValue<string>(out var openAiText))
            return openAiText;

        // Gemini shape.
        if (root?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"] is JsonValue gemini
            && gemini.TryGetValue<string>(out var geminiText))
            return geminiText;

        return FindFirstTextField(root, 0);
    }

    private static readonly string[] LikelyTextFields =
        ["content", "message", "text", "response", "answer", "result", "reply", "output", "data"];

    private static string? FindFirstTextField(JsonNode? node, int depth)
    {
        if (node is null || depth > 6)
            return null;

        if (node is JsonObject obj)
        {
            foreach (var key in LikelyTextFields)
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str))
                    return str;

            foreach (var key in LikelyTextFields)
                if (obj[key] is JsonObject or JsonArray)
                    if (FindFirstTextField(obj[key], depth + 1) is { } nested)
                        return nested;

            foreach (var (_, child) in obj)
                if (FindFirstTextField(child, depth + 1) is { } any)
                    return any;
        }

        if (node is JsonArray array)
            foreach (var entry in array)
                if (FindFirstTextField(entry, depth + 1) is { } found)
                    return found;

        return null;
    }
}
