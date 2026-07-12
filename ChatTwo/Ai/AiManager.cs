using System.Text.Json.Nodes;
using ChatTwo.Ui.Handler;
using ChatTwo.Util;
using Dalamud.Interface.ImGuiNotification;

namespace ChatTwo.Ai;

/// <summary>
/// The AI portal: dispatches requests to the configured provider and drives
/// the grammar correction / translation features of the chat input.
/// </summary>
public class AiManager : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly OpenAiProvider OpenAi = new();
    private readonly GeminiProvider Gemini = new();
    private readonly SwuAiProvider SwuAi = new();

    /// <summary> True while an AI request is in flight. </summary>
    public bool Busy { get; private set; }

    /// <summary> The pending result shown in the suggestion panel. </summary>
    public AiSuggestion? Suggestion { get; private set; }

    /// <summary> The input text as it was before the last applied suggestion. </summary>
    public string? LastOriginalInput { get; private set; }

    public void Dispose()
    {
        AiUtil.HttpClient.Dispose();
    }

    public IAiProvider CurrentProvider => GetProvider(Plugin.Config.AiProvider);

    public IAiProvider GetProvider(AiProviderType type) => type switch
    {
        AiProviderType.OpenAi => OpenAi,
        AiProviderType.Gemini => Gemini,
        AiProviderType.SwuAi => SwuAi,
        _ => OpenAi,
    };

    #region Response cache
    // Successful responses are cached so repeating a request (re-checking an
    // unchanged sentence, re-translating a common message like "gg") is
    // instant and costs no API quota. The key includes provider, model and
    // prompt, so changing any of them naturally invalidates old entries.
    private const int CacheLimit = 200;
    private readonly object CacheLock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, string Corrected, List<string> Explanations)>> CacheMap = new();
    private readonly LinkedList<(string Key, string Corrected, List<string> Explanations)> CacheOrder = new();

    public int CacheCount
    {
        get
        {
            lock (CacheLock)
                return CacheMap.Count;
        }
    }

    public void ClearCache()
    {
        lock (CacheLock)
        {
            CacheMap.Clear();
            CacheOrder.Clear();
        }
    }

    private static string CurrentModel => Plugin.Config.AiProvider switch
    {
        AiProviderType.OpenAi => Plugin.Config.OpenAiModel,
        AiProviderType.Gemini => Plugin.Config.GeminiModel,
        AiProviderType.SwuAi => Plugin.Config.SwuAiModel,
        _ => string.Empty,
    };

    private static string CacheKey(AiMode mode, string prompt, string text)
    {
        return $"{Plugin.Config.AiProvider}{CurrentModel}{mode}{prompt}{text}";
    }

    private bool TryGetCached(string key, out (string Corrected, List<string> Explanations) result)
    {
        lock (CacheLock)
        {
            if (CacheMap.TryGetValue(key, out var node))
            {
                // Mark as most recently used.
                CacheOrder.Remove(node);
                CacheOrder.AddFirst(node);
                result = (node.Value.Corrected, node.Value.Explanations);
                return true;
            }
        }

        result = default;
        return false;
    }

    private void StoreInCache(string key, string corrected, List<string> explanations)
    {
        lock (CacheLock)
        {
            if (CacheMap.ContainsKey(key))
                return;

            var node = CacheOrder.AddFirst((key, corrected, explanations));
            CacheMap[key] = node;

            if (CacheMap.Count <= CacheLimit)
                return;

            var oldest = CacheOrder.Last!;
            CacheMap.Remove(oldest.Value.Key);
            CacheOrder.RemoveLast();
        }
    }
    #endregion

    /// <summary>
    /// Runs the given AI mode over the text and parses the structured reply
    /// into the corrected/translated message and its Thai explanations.
    /// Successful results are served from an LRU cache when repeated.
    /// </summary>
    public async Task<(string Corrected, List<string> Explanations)> RunAsync(AiMode mode, string text, CancellationToken token, RewriteStyle? style = null)
    {
        var prompt = mode switch
        {
            AiMode.Grammar => Plugin.Config.AiGrammarPrompt,
            AiMode.Translate => Plugin.Config.AiTranslatePrompt,
            AiMode.Rewrite => Plugin.Config.AiRewritePrompt.Replace("{style}", (style ?? RewriteStyle.Politer).Instruction()),
            _ => Plugin.Config.AiExplainPrompt,
        };

        text = text.Trim();
        var key = CacheKey(mode, prompt, text);
        if (TryGetCached(key, out var cached))
            return cached;

        var reply = await CurrentProvider.ChatAsync(prompt, text, token);
        var (corrected, explanations) = ParseStructuredReply(reply);

        // Collapse newlines; chat messages are single-line.
        corrected = corrected.ReplaceLineEndings(" ").Trim();

        StoreInCache(key, corrected, explanations);
        return (corrected, explanations);
    }

    /// <summary>
    /// Requests a suggestion for the current chat input in the background and
    /// shows it in the suggestion panel. Commands keep their "/command "
    /// prefix untouched.
    /// </summary>
    public void RequestSuggestion(InputHandler handler, AiMode mode, RewriteStyle? style = null)
    {
        // Also guards the keybinds, which are checked regardless of AI state.
        if (!Plugin.Config.AiEnabled || Busy)
            return;

        var original = handler.ChatInput;
        var prefix = string.Empty;
        var text = original;

        if (text.TrimStart().StartsWith('/'))
        {
            var spaceIdx = text.IndexOf(' ');
            if (spaceIdx == -1)
                return; // A bare command has nothing to correct.

            prefix = text[..(spaceIdx + 1)];
            text = text[(spaceIdx + 1)..];
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        RunSuggestionRequest(handler, mode, style, text, prefix, original);
    }

    /// <summary>
    /// Rewrites the currently shown suggestion in a different tone, so styles
    /// can be chained (grammar fix, then politer, then shorter) without
    /// applying in between.
    /// </summary>
    public void RequestRestyle(InputHandler handler, RewriteStyle style)
    {
        if (!Plugin.Config.AiEnabled || Busy || Suggestion is not { } current || current.Mode == AiMode.Explain)
            return;

        RunSuggestionRequest(handler, AiMode.Rewrite, style, current.Corrected, current.Prefix, current.OriginalInput);
    }

    private void RunSuggestionRequest(InputHandler handler, AiMode mode, RewriteStyle? style, string text, string prefix, string originalInput)
    {
        Busy = true;
        Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var (corrected, explanations) = await RunAsync(mode, text, cts.Token, style);

                var suggestion = new AiSuggestion
                {
                    Mode = mode,
                    OriginalInput = originalInput,
                    Prefix = prefix,
                    Corrected = corrected,
                    Style = style,
                    Explanations = explanations,
                    // A translation has nothing meaningful to diff against.
                    Words = mode is AiMode.Grammar or AiMode.Rewrite
                        ? AiSuggestion.DiffWords(text, corrected)
                        : corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => (word, false)).ToList(),
                };

                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    // Don't show a stale suggestion if the user changed the
                    // input while the request was running.
                    if (handler.ChatInput == originalInput)
                        Suggestion = suggestion;
                });
            }
            catch (OperationCanceledException)
            {
                WrapperUtil.AddNotification("AI request timed out", NotificationType.Error);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "AI request failed");
                WrapperUtil.AddNotification($"AI request failed: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                Busy = false;
            }
        });
    }

    /// <summary>
    /// Translates a received message into Thai and shows it in the panel.
    /// Nothing gets applied to the input; the panel is informational only.
    /// </summary>
    public void RequestExplanation(string messageText)
    {
        if (!Plugin.Config.AiEnabled || Busy || string.IsNullOrWhiteSpace(messageText))
            return;

        Busy = true;
        Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var (translated, explanations) = await RunAsync(AiMode.Explain, messageText, cts.Token);

                var suggestion = new AiSuggestion
                {
                    Mode = AiMode.Explain,
                    OriginalInput = messageText,
                    Prefix = string.Empty,
                    Corrected = translated,
                    Explanations = explanations,
                };

                await Plugin.Framework.RunOnFrameworkThread(() => Suggestion = suggestion);
            }
            catch (OperationCanceledException)
            {
                WrapperUtil.AddNotification("AI request timed out", NotificationType.Error);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "AI explanation failed");
                WrapperUtil.AddNotification($"AI request failed: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                Busy = false;
            }
        });
    }

    /// <summary> Puts the suggestion into the chat input and closes the panel. </summary>
    public void ApplySuggestion(InputHandler handler)
    {
        if (Suggestion is null)
            return;

        var result = Suggestion.Prefix + Suggestion.Corrected;
        if (result.Length > 500)
            result = result[..500];

        LastOriginalInput = handler.ChatInput;
        handler.ChatInput = result;
        handler.Activate = true;
        Suggestion = null;
    }

    public void DismissSuggestion()
    {
        Suggestion = null;
    }

    /// <summary> Restores the input text from before the last applied suggestion. </summary>
    public void RevertInput(InputHandler handler)
    {
        if (LastOriginalInput is null)
            return;

        handler.ChatInput = LastOriginalInput;
        handler.Activate = true;
        LastOriginalInput = null;
    }

    /// <summary>
    /// Parses a reply that should be {"corrected": ..., "explanations": [...]}
    /// but tolerates markdown fences and plain-text replies.
    /// </summary>
    public static (string Corrected, List<string> Explanations) ParseStructuredReply(string reply)
    {
        var text = reply.Trim();

        // Strip ```json ... ``` fences some models insist on.
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline != -1)
                text = text[(firstNewline + 1)..];
            if (text.TrimEnd().EndsWith("```"))
                text = text.TrimEnd()[..^3];
            text = text.Trim();
        }

        try
        {
            var json = JsonNode.Parse(text);
            var corrected = json?["corrected"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(corrected))
                return (reply.Trim(), []);

            var explanations = new List<string>();
            if (json?["explanations"] is JsonArray array)
                foreach (var entry in array)
                    if (entry?.GetValue<string>() is { } explanation && !string.IsNullOrWhiteSpace(explanation))
                        explanations.Add(explanation);

            return (corrected, explanations);
        }
        catch (Exception)
        {
            // Model ignored the JSON instruction; treat the reply as the text.
            return (reply.Trim(), []);
        }
    }
}
