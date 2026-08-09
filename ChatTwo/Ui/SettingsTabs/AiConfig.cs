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

        ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudOrange, "Your message, and the recent conversation when context is enabled, is sent to the selected AI service. API keys are stored encrypted for your Windows user.");
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
            case AiProviderType.Grok:
                ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudGrey, "Create an API key in the xAI console (console.x.ai).");
                PasswordInput("API key##grok-key", ref Mutable.GrokApiKey);
                TextInput("Model##grok-model", ref Mutable.GrokModel);
                foreach (var model in GrokProvider.KnownModels)
                {
                    if (ImGui.SmallButton($"{model}##grok-pick-{model}"))
                        Mutable.GrokModel = model;

                    ImGui.SameLine();
                }
                ImGui.NewLine();

                if (GrokProvider.SupportsReasoningEffort(Mutable.GrokModel))
                {
                    ImGui.Spacing();
                    using var combo = ImGuiUtil.BeginComboVertical("Reasoning effort", Mutable.GrokReasoningEffort);
                    if (combo)
                        foreach (var effort in new[] { "none", "low", "medium", "high" })
                            if (ImGui.Selectable(effort, effort == Mutable.GrokReasoningEffort))
                                Mutable.GrokReasoningEffort = effort;

                    ImGuiUtil.HelpText("Reasoning tokens are billed as output. Translation does not need them, so \"none\" is both cheaper and faster.");
                }
                else
                {
                    ImGuiUtil.HelpText("Only grok-4.3 can turn reasoning off, which makes it noticeably cheaper for translation.");
                }
                break;
        }

        ImGui.Spacing();
        DrawModelList();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawContextSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawUsageMeter();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawCustomStyles();

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

    private void DrawContextSettings()
    {
        ImGuiUtil.OptionCheckbox(ref Mutable.AiContextEnabled, "Use conversation context",
            "Sends the recent conversation along with your request so the AI knows who is speaking, the situation and the writing style. Strongly improves roleplay translation.");

        if (!Mutable.AiContextEnabled)
            return;

        using var indent = ImRaii.PushIndent();

        ImGui.TextUnformatted("Use context for:");
        ImGui.Checkbox("Translating incoming messages##ctx-explain", ref Mutable.AiContextForExplain);
        ImGui.Checkbox("Thai to English##ctx-translate", ref Mutable.AiContextForTranslate);
        ImGui.Checkbox("Rewrite styles##ctx-rewrite", ref Mutable.AiContextForRewrite);
        ImGui.Checkbox("English grammar correction##ctx-grammar", ref Mutable.AiContextForGrammar);
        ImGuiUtil.HelpText("Grammar correction only looks at your own sentence, so context is off by default to save tokens.");
        ImGui.Spacing();

        if (ImGuiUtil.InputIntVertical("Context budget (tokens)", "The scene is kept until it reaches this size, then restarts. Bigger means better continuity but a larger prompt.", ref Mutable.AiContextMaxTokens, 100, 500))
            Mutable.AiContextMaxTokens = Math.Clamp(Mutable.AiContextMaxTokens, 200, 8000);
        ImGui.Spacing();

        if (ImGuiUtil.InputIntVertical("Forget the scene after (minutes)", "A pause longer than this starts a fresh scene.", ref Mutable.AiContextIdleMinutes))
            Mutable.AiContextIdleMinutes = Math.Clamp(Mutable.AiContextIdleMinutes, 1, 120);
        ImGui.Spacing();

        if (ImGuiUtil.InputIntVertical("Skip context below (characters)", "Short messages like \"ty\" gain nothing from context, and stay cheap and cacheable without it.", ref Mutable.AiContextMinChars, 5, 20))
            Mutable.AiContextMinChars = Math.Clamp(Mutable.AiContextMinChars, 0, 500);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.AiExplanationsInReading, "Thai notes when reading messages",
            "Adds vocabulary notes when translating incoming messages. Turning this off makes replies shorter and cheaper.");
        ImGui.Spacing();

        var scene = Plugin.AiManager.Scenes.Get(Plugin.CurrentTab.Identifier);
        ImGui.TextUnformatted($"Current tab scene: {scene.LineCount} lines, ~{scene.TokenEstimate} tokens");
        if (ImGui.Button("Clear scene context"))
            Plugin.AiManager.Scenes.ResetAll();
    }

    private void DrawUsageMeter()
    {
        var usage = Plugin.AiManager.Usage;
        usage.RollMonthIfNeeded();

        ImGui.TextUnformatted("Usage");
        ImGuiUtil.HelpText("Measured from the token counts the AI service reports, so this is what you actually spent, not an estimate of your typing.");
        ImGui.Spacing();

        var hasPricing = AiUsageTracker.TryGetPricing(AiManager.CurrentModel, out _);

        ImGui.TextUnformatted($"This session: {usage.Requests} requests, {usage.SessionInput:N0} in / {usage.SessionOutput:N0} out tokens");
        if (hasPricing)
            ImGui.TextUnformatted($"This session cost: {usage.SessionCostThb:N2} THB");

        if (usage.SessionInput > 0)
        {
            ImGuiUtil.WrappedTextWithColor(usage.CachedShare > 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey,
                $"Served from cache: {usage.CachedShare * 100:N0}% of input tokens ({usage.SessionCached:N0})");
            if (usage.SessionReasoning > 0)
                ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudOrange,
                    $"Spent on hidden reasoning: {usage.SessionReasoning:N0} output tokens (set reasoning effort to \"none\" to avoid this)");
        }

        ImGui.Spacing();
        if (hasPricing)
        {
            ImGui.TextUnformatted($"This month: {usage.MonthCostThb:N2} of {Mutable.AiMonthlyBudgetThb:N0} THB");
            var fraction = Mutable.AiMonthlyBudgetThb > 0 ? (float)(usage.MonthCostThb / Mutable.AiMonthlyBudgetThb) : 0f;
            ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new System.Numerics.Vector2(-1, 0));
        }
        else
        {
            ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudGrey, $"No price list for \"{AiManager.CurrentModel}\", so only token counts are tracked.");
        }

        ImGui.Spacing();
        if (ImGuiUtil.DragFloatVertical("Monthly budget (THB)", ref Mutable.AiMonthlyBudgetThb, 5f, 0f, 10000f, "%.0f"))
            Mutable.AiMonthlyBudgetThb = Math.Clamp(Mutable.AiMonthlyBudgetThb, 0f, 10000f);
        ImGuiUtil.HelpText("A warning appears at 80%. At 100% AI requests stop until you resume below. Set to 0 to disable the brake.");
        ImGui.Spacing();

        if (ImGuiUtil.DragFloatVertical("USD to THB rate", ref Mutable.AiUsdToThb, 0.5f, 1f, 200f, "%.1f"))
            Mutable.AiUsdToThb = Math.Clamp(Mutable.AiUsdToThb, 1f, 200f);
        ImGui.Spacing();

        if (!usage.CanSpend())
        {
            ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudRed, "Monthly budget reached. AI requests are paused.");
            if (ImGui.Button("Resume anyway for this session"))
                usage.BudgetOverridden = true;
            ImGui.SameLine();
        }

        if (ImGui.Button("Reset session counter"))
            usage.ResetSession();

        ImGui.SameLine();
        if (ImGuiUtil.CtrlShiftButton("Reset month", "Hold Ctrl and Shift to reset this month's total"))
            usage.ResetMonth();
    }

    private void DrawCustomStyles()
    {
        ImGui.TextUnformatted("Custom rewrite styles");
        ImGuiUtil.HelpText("Your own tones, shown next to Politer, Friendlier and Shorter in the right click menu and the suggestion panel.");
        ImGui.Spacing();

        AiCustomStyle? remove = null;
        foreach (var (style, index) in Mutable.AiCustomStyles.Select((style, index) => (style, index)))
        {
            using var id = ImRaii.PushId($"ai-style-{index}");

            ImGui.SetNextItemWidth(200f);
            ImGui.InputTextWithHint("##name", "Name", ref style.Name, 64);
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(Dalamud.Interface.FontAwesomeIcon.Trash, tooltip: "Remove this style"))
                remove = style;

            ImGui.InputTextWithHint("##instruction", "Instruction sent to the AI, e.g. \"Rewrite the message in a calm, formal tone.\"", ref style.Instruction, 1000);
            ImGui.Spacing();
        }

        if (remove != null)
            Mutable.AiCustomStyles.Remove(remove);

        if (ImGui.Button("Add style"))
            Mutable.AiCustomStyles.Add(new AiCustomStyle { Name = "New style", Instruction = "Rewrite the message " });
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
                    case AiProviderType.Grok:
                        Mutable.GrokModel = model;
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
