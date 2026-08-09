namespace ChatTwo.Ai;

[Serializable]
public enum AiProviderType
{
    OpenAi = 0,
    Gemini = 1,
    SwuAi = 2,
    Grok = 3,
}

public static class AiProviderTypeExt
{
    public static string Name(this AiProviderType type) => type switch
    {
        AiProviderType.OpenAi => "ChatGPT (OpenAI)",
        AiProviderType.Gemini => "Gemini (Google)",
        AiProviderType.SwuAi => "SWU AI",
        AiProviderType.Grok => "Grok (xAI)",
        _ => type.ToString(),
    };
}

/// <summary>
/// A single-turn request. The parts are kept separate so providers can lay
/// them out in the order that caches best: everything stable first
/// (instructions), then the append-only transcript, then the new text.
/// </summary>
public class AiRequest
{
    public required string SystemPrompt;
    /// <summary> Append-only conversation transcript, or null for no context. </summary>
    public string? Context;
    public required string UserText;
    /// <summary> Stable per scene; lets xAI route us to our own cache. </summary>
    public string? ConversationId;
    public int MaxOutputTokens;
}

public class AiResponse
{
    public required string Text;
    public int InputTokens;
    public int OutputTokens;
    /// <summary> Part of InputTokens that was billed at the cached rate. </summary>
    public int CachedTokens;
    /// <summary> Part of OutputTokens spent on hidden reasoning. </summary>
    public int ReasoningTokens;
}

public interface IAiProvider
{
    /// <summary>
    /// Sends a single-turn chat request and returns the model's reply along
    /// with token usage. Throws on network errors, bad configuration or
    /// unparsable responses.
    /// </summary>
    Task<AiResponse> ChatAsync(AiRequest request, CancellationToken token);

    /// <summary>
    /// Returns the model names available to the configured account, or an
    /// empty list if the provider does not support listing models.
    /// </summary>
    Task<List<string>> GetModelsAsync(CancellationToken token);
}
