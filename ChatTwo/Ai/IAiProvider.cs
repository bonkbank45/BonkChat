namespace ChatTwo.Ai;

[Serializable]
public enum AiProviderType
{
    OpenAi = 0,
    Gemini = 1,
    SwuAi = 2,
}

public static class AiProviderTypeExt
{
    public static string Name(this AiProviderType type) => type switch
    {
        AiProviderType.OpenAi => "ChatGPT (OpenAI)",
        AiProviderType.Gemini => "Gemini (Google)",
        AiProviderType.SwuAi => "SWU AI",
        _ => type.ToString(),
    };
}

public interface IAiProvider
{
    /// <summary>
    /// Sends a single-turn chat request and returns the model's text reply.
    /// Throws on network errors, bad configuration or unparsable responses.
    /// </summary>
    Task<string> ChatAsync(string systemPrompt, string userText, CancellationToken token);

    /// <summary>
    /// Returns the model names available to the configured account, or an
    /// empty list if the provider does not support listing models.
    /// </summary>
    Task<List<string>> GetModelsAsync(CancellationToken token);
}
