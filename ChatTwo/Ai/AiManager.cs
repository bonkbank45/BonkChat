using System.Text;
using System.Text.Json.Nodes;
using ChatTwo.Ui.Handler;
using ChatTwo.Util;
using Dalamud.Interface.ImGuiNotification;

namespace ChatTwo.Ai;

/// <summary>
/// The AI portal: builds requests (prompt, scene context, output format),
/// dispatches them to the configured provider and drives the suggestion panel.
/// </summary>
public class AiManager : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private Plugin Plugin { get; }

    private readonly OpenAiProvider OpenAi = new();
    private readonly GeminiProvider Gemini = new();
    private readonly SwuAiProvider SwuAi = new();
    private readonly GrokProvider Grok = new();

    public readonly SceneBufferManager Scenes = new();
    public readonly AiUsageTracker Usage;

    public AiManager(Plugin plugin)
    {
        Plugin = plugin;
        Usage = new AiUsageTracker(plugin);
    }

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
        AiProviderType.Grok => Grok,
        _ => OpenAi,
    };

    public static string CurrentModel => Plugin.Config.AiProvider switch
    {
        AiProviderType.OpenAi => Plugin.Config.OpenAiModel,
        AiProviderType.Gemini => Plugin.Config.GeminiModel,
        AiProviderType.SwuAi => Plugin.Config.SwuAiModel,
        AiProviderType.Grok => Plugin.Config.GrokModel,
        _ => string.Empty,
    };

    #region Prompt assembly
    /// <summary> Reading modes answer in Thai and can skip the teaching notes. </summary>
    private static bool IsReadingMode(AiMode mode) => mode is AiMode.Explain;

    private static bool WantsExplanations(AiMode mode)
    {
        return !IsReadingMode(mode) || Plugin.Config.AiExplanationsInReading;
    }

    private static bool ContextEnabledFor(AiMode mode)
    {
        if (!Plugin.Config.AiContextEnabled)
            return false;

        return mode switch
        {
            AiMode.Grammar => Plugin.Config.AiContextForGrammar,
            AiMode.Translate => Plugin.Config.AiContextForTranslate,
            AiMode.Rewrite => Plugin.Config.AiContextForRewrite,
            AiMode.Explain => Plugin.Config.AiContextForExplain,
            _ => false,
        };
    }

    /// <summary>
    /// Builds the system prompt: the user's task prompt, then the rules the
    /// code owns (context handling, no emoji, output format). Everything here
    /// is stable across requests of the same mode, which is what lets the
    /// provider serve it from cache.
    /// </summary>
    private static string BuildSystemPrompt(AiMode mode, string? styleInstruction, bool hasContext)
    {
        var prompt = new StringBuilder(mode switch
        {
            AiMode.Grammar => Plugin.Config.AiGrammarPrompt,
            AiMode.Translate => Plugin.Config.AiTranslatePrompt,
            AiMode.Rewrite => Plugin.Config.AiRewritePrompt.Replace("{style}", styleInstruction ?? AiStyle.BuiltIn[0].Instruction),
            _ => Plugin.Config.AiExplainPrompt,
        });

        if (hasContext)
            prompt.Append(' ').Append(Configuration.ContextRule);

        prompt.Append(Configuration.NoEmojiRule);

        prompt.Append(WantsExplanations(mode)
            ? Configuration.JsonFormatRule
            : Configuration.PlainFormatRule);

        return prompt.ToString();
    }
    #endregion

    #region Response cache
    // Successful responses are cached so repeating a request (re-checking an
    // unchanged sentence, re-translating a common message like "gg") is
    // instant and costs no API quota. The key includes provider, model,
    // prompt and context, so anything that would change the answer misses.
    private const int CacheLimit = 200;
    private readonly Lock CacheLock = new();
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
    /// Runs the given AI mode over the text and parses the reply into the
    /// resulting message and its Thai explanations. Successful results are
    /// served from an LRU cache when repeated.
    /// </summary>
    public async Task<(string Corrected, List<string> Explanations)> RunAsync(
        AiMode mode, string text, CancellationToken token, string? styleInstruction = null, Guid? tabId = null)
    {
        if (!Usage.CanSpend())
            throw new InvalidOperationException("Monthly AI budget reached; raise it or resume in the AI settings");

        text = text.Trim();

        // Short messages gain nothing from context, so they stay cheap and
        // cacheable across the whole session.
        var scene = tabId is { } id && ContextEnabledFor(mode) && text.Length >= Plugin.Config.AiContextMinChars
            ? Scenes.Get(id)
            : null;
        var context = scene?.Build();

        var systemPrompt = BuildSystemPrompt(mode, styleInstruction, context != null);

        var key = $"{Plugin.Config.AiProvider}{CurrentModel}{systemPrompt}{context}{text}";
        if (TryGetCached(key, out var cached))
            return cached;

        var response = await CurrentProvider.ChatAsync(new AiRequest
        {
            SystemPrompt = systemPrompt,
            Context = context,
            UserText = text,
            ConversationId = scene?.ConversationId,
            MaxOutputTokens = Plugin.Config.AiMaxOutputTokens,
        }, token);

        Usage.Record(CurrentModel, response);

        var (corrected, explanations) = ParseStructuredReply(response.Text);

        // Collapse newlines; chat messages are single-line. Strip emoji as a
        // hard guarantee on top of the prompt instruction: the game chat and
        // the panel font can't render them.
        corrected = StripEmoji(corrected.ReplaceLineEndings(" ")).Trim();
        explanations = explanations.Select(e => StripEmoji(e).Trim()).Where(e => e.Length > 0).ToList();

        StoreInCache(key, corrected, explanations);
        return (corrected, explanations);
    }

    /// <summary>
    /// Requests a suggestion for the current chat input in the background and
    /// shows it in the suggestion panel. Commands keep their "/command "
    /// prefix untouched.
    /// </summary>
    public void RequestSuggestion(InputHandler handler, AiMode mode, AiStyle? style = null)
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
    public void RequestRestyle(InputHandler handler, AiStyle style)
    {
        if (!Plugin.Config.AiEnabled || Busy || Suggestion is not { } current || current.Mode == AiMode.Explain)
            return;

        RunSuggestionRequest(handler, AiMode.Rewrite, style, current.Corrected, current.Prefix, current.OriginalInput);
    }

    private void RunSuggestionRequest(InputHandler handler, AiMode mode, AiStyle? style, string text, string prefix, string originalInput)
    {
        var tabId = Plugin.CurrentTab.Identifier;

        Busy = true;
        Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var (corrected, explanations) = await RunAsync(mode, text, cts.Token, style?.Instruction, tabId);

                var suggestion = new AiSuggestion
                {
                    Mode = mode,
                    OriginalInput = originalInput,
                    Prefix = prefix,
                    Corrected = corrected,
                    StyleName = style?.Name,
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

        var tabId = Plugin.CurrentTab.Identifier;

        Busy = true;
        Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var (translated, explanations) = await RunAsync(AiMode.Explain, messageText, cts.Token, tabId: tabId);

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
            // Plain-text reply (concise mode) or the model ignored the format.
            return (reply.Trim(), []);
        }
    }

    /// <summary>
    /// Removes emoji: all astral-plane characters (surrogate pairs), zero
    /// width joiners and variation selectors. BMP text (Latin, Thai, JP and
    /// the symbols the game does support) passes through untouched.
    /// </summary>
    public static string StripEmoji(string text)
    {
        if (!text.Any(c => char.IsSurrogate(c) || c is '️' or '‍'))
            return text;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsSurrogate(c) || c is '️' or '‍')
                continue;

            builder.Append(c);
        }

        // Collapse double spaces left behind by removed emoji.
        return builder.Replace("  ", " ").ToString();
    }
}
