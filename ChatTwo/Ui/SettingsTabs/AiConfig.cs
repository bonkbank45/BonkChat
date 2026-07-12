using ChatTwo.Ai;
using ChatTwo.Util;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace ChatTwo.Ui.SettingsTabs;

public sealed class AiConfig(Plugin plugin, Configuration mutable) : ISettingsTab
{
    private Plugin Plugin { get; } = plugin;
    private Configuration Mutable { get; } = mutable;
    public string Name => "AI###tabs-Ai";

    private bool TestRunning;
    private bool ModelsLoading;
    private List<string> AvailableModels = [];

    public void Draw(bool changed)
    {
        ImGuiUtil.OptionCheckbox(ref Mutable.AiEnabled, "Enable AI features", "Shows the grammar correction button next to the chat input.");
        ImGui.Spacing();

        if (!Mutable.AiEnabled)
            return;

        ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudOrange, "Your message is sent to the selected AI service when you press the correction button. API keys are stored in the plugin configuration in plain text.");
        ImGui.Spacing();

        using (var combo = ImGuiUtil.BeginComboVertical("Provider", Mutable.AiProvider.Name()))
        {
            if (combo)
            {
                foreach (var type in Enum.GetValues<AiProviderType>())
                {
                    if (ImGui.Selectable(type.Name(), type == Mutable.AiProvider))
                    {
                        Mutable.AiProvider = type;
                        AvailableModels = [];
                    }
                }
            }
        }
        ImGui.Spacing();

        switch (Mutable.AiProvider)
        {
            case AiProviderType.OpenAi:
                PasswordInput("API key##openai-key", ref Mutable.OpenAiApiKey);
                TextInput("Model##openai-model", ref Mutable.OpenAiModel);
                break;
            case AiProviderType.Gemini:
                PasswordInput("API key##gemini-key", ref Mutable.GeminiApiKey);
                TextInput("Model##gemini-model", ref Mutable.GeminiModel);
                break;
            case AiProviderType.SwuAi:
                ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudGrey, "Request an API key and user ID by registering on the SWU AI system (swuai.swu.ac.th).");
                PasswordInput("API key##swu-key", ref Mutable.SwuAiApiKey);
                TextInput("User ID##swu-user", ref Mutable.SwuAiUserId);
                TextInput("Model##swu-model", ref Mutable.SwuAiModel);
                break;
        }

        ImGui.Spacing();
        DrawModelList();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Keybinds (work while typing in the chat input)");
        ImGuiUtil.HelpText("Click a button and press a key combination. Esc clears the keybind.");
        ImGui.Spacing();

        ImGui.TextUnformatted("Correct grammar");
        ImGuiUtil.KeybindInput("AiGrammarKeybind", ref Mutable.AiGrammarKeybind);
        ImGui.Spacing();

        ImGui.TextUnformatted("Translate to English");
        ImGuiUtil.KeybindInput("AiTranslateKeybind", ref Mutable.AiTranslateKeybind);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Grammar correction prompt");
        ImGui.InputTextMultiline("##ai-grammar-prompt", ref Mutable.AiGrammarPrompt, 2000, new System.Numerics.Vector2(-1, 100));
        if (ImGui.Button("Reset##ai-grammar-reset"))
            Mutable.AiGrammarPrompt = Configuration.DefaultGrammarPrompt;

        ImGui.Spacing();
        ImGui.TextUnformatted("Translation prompt");
        ImGui.InputTextMultiline("##ai-translate-prompt", ref Mutable.AiTranslatePrompt, 2000, new System.Numerics.Vector2(-1, 100));
        if (ImGui.Button("Reset##ai-translate-reset"))
            Mutable.AiTranslatePrompt = Configuration.DefaultTranslatePrompt;

        ImGui.Spacing();
        ImGui.TextUnformatted("Message explanation prompt (right click a message)");
        ImGui.InputTextMultiline("##ai-explain-prompt", ref Mutable.AiExplainPrompt, 2000, new System.Numerics.Vector2(-1, 100));
        if (ImGui.Button("Reset##ai-explain-reset"))
            Mutable.AiExplainPrompt = Configuration.DefaultExplainPrompt;

        ImGui.Spacing();
        ImGui.TextUnformatted("Rewrite prompt ({style} is replaced by the chosen tone)");
        ImGui.InputTextMultiline("##ai-rewrite-prompt", ref Mutable.AiRewritePrompt, 2000, new System.Numerics.Vector2(-1, 100));
        if (ImGui.Button("Reset##ai-rewrite-reset"))
            Mutable.AiRewritePrompt = Configuration.DefaultRewritePrompt;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var cacheCount = Plugin.AiManager.CacheCount;
        ImGui.TextUnformatted($"Cached AI responses: {cacheCount}");
        ImGuiUtil.HelpText("Repeating an identical request is answered from this cache instantly, without calling the AI service.");
        using (ImRaii.Disabled(cacheCount == 0))
        {
            if (ImGui.Button("Clear cache"))
                Plugin.AiManager.ClearCache();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // The test uses the saved configuration, not the mutable copy, so ask
        // the user to apply their changes first.
        ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudGrey, "Save your settings before testing. The test sends \"how i can goes there?\" through the configured provider.");
        using (ImRaii.Disabled(TestRunning))
        {
            if (ImGui.Button(TestRunning ? "Testing..." : "Test connection"))
            {
                TestRunning = true;
                Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        var (corrected, explanations) = await Plugin.AiManager.RunAsync(AiMode.Grammar, "how i can goes there?", cts.Token);
                        WrapperUtil.AddNotification($"AI test succeeded: {corrected} ({explanations.Count} explanations)", NotificationType.Success);
                    }
                    catch (Exception ex)
                    {
                        WrapperUtil.AddNotification($"AI test failed: {ex.Message}", NotificationType.Error);
                    }
                    finally
                    {
                        TestRunning = false;
                    }
                });
            }
        }
    }

    private void DrawModelList()
    {
        using (ImRaii.Disabled(ModelsLoading))
        {
            if (ImGui.Button(ModelsLoading ? "Loading models..." : "Fetch available models"))
            {
                ModelsLoading = true;
                var provider = Plugin.AiManager.GetProvider(Mutable.AiProvider);
                Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        AvailableModels = await provider.GetModelsAsync(cts.Token);
                        if (AvailableModels.Count == 0)
                            WrapperUtil.AddNotification("No models returned", NotificationType.Warning);
                    }
                    catch (Exception ex)
                    {
                        WrapperUtil.AddNotification($"Fetching models failed: {ex.Message}", NotificationType.Error);
                    }
                    finally
                    {
                        ModelsLoading = false;
                    }
                });
            }
        }
        ImGuiUtil.HelpText("Uses the saved API key, so save your settings first. Click a model to select it.");

        if (AvailableModels.Count == 0)
            return;

        using var child = ImRaii.Child("##ai-model-list", new System.Numerics.Vector2(-1, 150), true);
        if (!child)
            return;

        foreach (var model in AvailableModels)
        {
            if (ImGui.Selectable(model))
            {
                switch (Mutable.AiProvider)
                {
                    case AiProviderType.OpenAi:
                        Mutable.OpenAiModel = model;
                        break;
                    case AiProviderType.Gemini:
                        Mutable.GeminiModel = model;
                        break;
                    case AiProviderType.SwuAi:
                        Mutable.SwuAiModel = model;
                        break;
                }
            }
        }
    }

    private static void TextInput(string label, ref string value)
    {
        ImGui.TextUnformatted(label[..label.IndexOf("##", StringComparison.Ordinal)]);
        ImGui.SetNextItemWidth(350f);
        ImGui.InputText($"##{label}", ref value, 512);
        ImGui.Spacing();
    }

    private static void PasswordInput(string label, ref string value)
    {
        ImGui.TextUnformatted(label[..label.IndexOf("##", StringComparison.Ordinal)]);
        ImGui.SetNextItemWidth(350f);
        ImGui.InputText($"##{label}", ref value, 512, ImGuiInputTextFlags.Password);
        if (SecretUtil.IsSealed(value))
            ImGuiUtil.HelpText("Stored encrypted (Windows DPAPI). Type a new key to replace it.");
        ImGui.Spacing();
    }
}
