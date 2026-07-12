using ChatTwo.Ui.Handler;
using ChatTwo.Util;
using Dalamud.Interface.ImGuiNotification;

namespace ChatTwo.Ai;

/// <summary>
/// The AI portal: dispatches requests to the configured provider and drives
/// the grammar correction feature of the chat input.
/// </summary>
public class AiManager
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly OpenAiProvider OpenAi = new();
    private readonly GeminiProvider Gemini = new();
    private readonly SwuAiProvider SwuAi = new();

    /// <summary> True while a correction request is in flight. </summary>
    public bool Busy { get; private set; }

    /// <summary> The input text as it was before the last applied correction. </summary>
    public string? LastOriginalInput { get; private set; }

    public IAiProvider CurrentProvider => GetProvider(Plugin.Config.AiProvider);

    public IAiProvider GetProvider(AiProviderType type) => type switch
    {
        AiProviderType.OpenAi => OpenAi,
        AiProviderType.Gemini => Gemini,
        AiProviderType.SwuAi => SwuAi,
        _ => OpenAi,
    };

    public async Task<string> CorrectGrammarAsync(string text, CancellationToken token)
    {
        return await CurrentProvider.ChatAsync(Plugin.Config.AiGrammarPrompt, text, token);
    }

    /// <summary>
    /// Corrects the current chat input in the background and writes the result
    /// back into the input box, keeping the original for reverting. Commands
    /// keep their "/command " prefix untouched.
    /// </summary>
    public void CorrectInput(InputHandler handler)
    {
        if (Busy)
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

        Busy = true;
        Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var corrected = await CorrectGrammarAsync(text, cts.Token);

                // Collapse newlines; chat messages are single-line.
                corrected = corrected.ReplaceLineEndings(" ").Trim();

                var result = prefix + corrected;
                if (result.Length > 500)
                    result = result[..500];

                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    // Don't clobber the input if the user changed it while the
                    // request was running.
                    if (handler.ChatInput != original)
                        return;

                    LastOriginalInput = original;
                    handler.ChatInput = result;
                    handler.Activate = true;
                });
            }
            catch (OperationCanceledException)
            {
                WrapperUtil.AddNotification("AI request timed out", NotificationType.Error);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "AI grammar correction failed");
                WrapperUtil.AddNotification($"AI correction failed: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                Busy = false;
            }
        });
    }

    /// <summary> Restores the input text from before the last correction. </summary>
    public void RevertInput(InputHandler handler)
    {
        if (LastOriginalInput is null)
            return;

        handler.ChatInput = LastOriginalInput;
        handler.Activate = true;
        LastOriginalInput = null;
    }
}
