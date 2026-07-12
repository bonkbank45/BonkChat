using System.Numerics;
using ChatTwo.Ai;
using ChatTwo.Util;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace ChatTwo.Ui;

/// <summary>
/// The AI suggestion panel as a floating window anchored directly above the
/// main chat window, so a large suggestion never shrinks the message log.
/// The height is measured while drawing and applied on the next frame, the
/// same way InputPreview does it.
/// </summary>
public class AiSuggestionWindow : Window
{
    private readonly Plugin Plugin;
    private float PanelHeight = 10;

    public AiSuggestionWindow(Plugin plugin) : base("##bonkchat-ai-suggestion")
    {
        Plugin = plugin;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar;

        DisableWindowSounds = true;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions()
    {
        return Plugin.Config.AiEnabled && Plugin.AiManager.Suggestion != null && !Plugin.ChatLog.IsHidden;
    }

    public override void PreDraw()
    {
        var pos = Plugin.ChatLog.LastWindowPos;
        var size = Plugin.ChatLog.LastWindowSize;

        Size = size with { Y = PanelHeight };
        Position = pos with { Y = pos.Y - PanelHeight };
        PositionCondition = ImGuiCond.Always;

        // Keep the panel readable regardless of the chat window transparency.
        BgAlpha = Math.Clamp(Plugin.Config.WindowAlpha / 100f, 0.85f, 1f);
    }

    public override void Draw()
    {
        if (Plugin.AiManager.Suggestion is not { } suggestion)
            return;

        var handler = Plugin.ChatLog.InputHandler;

        // The suggestion is stale once the input was sent or cleared, except
        // for explanations of received messages, which don't touch the input.
        if (suggestion.Mode != AiMode.Explain && handler.ChatInput.Length == 0)
        {
            Plugin.AiManager.DismissSuggestion();
            return;
        }

        var startY = ImGui.GetCursorPosY();

        var header = suggestion.Mode switch
        {
            AiMode.Grammar => "AI grammar suggestion:",
            AiMode.Translate => "AI translation:",
            AiMode.Rewrite => $"AI rewrite ({(suggestion.Style ?? RewriteStyle.Politer).Name().ToLowerInvariant()}):",
            _ => "AI translation to Thai:",
        };
        ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudViolet, header);

        if (suggestion.Mode == AiMode.Explain)
        {
            // Thai text has no spaces, so word-based rendering doesn't apply.
            ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudWhite, suggestion.Corrected);
        }
        else
        {
            // The suggested text, changed words highlighted, wrapped per word.
            foreach (var (word, changed) in suggestion.Words)
            {
                if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(word).X)
                    ImGui.NewLine();

                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, changed))
                    ImGui.TextUnformatted(word);

                ImGui.SameLine();
            }
            ImGui.NewLine();
        }

        foreach (var explanation in suggestion.Explanations)
            ImGuiUtil.WrappedTextWithColor(ImGuiColors.DalamudGrey, $"- {explanation}");

        if (suggestion.Mode != AiMode.Explain)
        {
            if (ImGui.Button("Apply##ai-apply"))
                Plugin.AiManager.ApplySuggestion(handler);

            ImGui.SameLine();
        }

        if (ImGui.Button("Dismiss##ai-dismiss"))
            Plugin.AiManager.DismissSuggestion();

        // Chain a tone rewrite on top of the current suggestion.
        if (suggestion.Mode != AiMode.Explain)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("|");

            using (ImRaii.Disabled(Plugin.AiManager.Busy))
            {
                foreach (var style in Enum.GetValues<RewriteStyle>())
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"{style.Name()}##ai-restyle-{style}"))
                        Plugin.AiManager.RequestRestyle(handler, style);
                }
            }
        }

        // Measured height is applied to the window size on the next frame.
        PanelHeight = ImGui.GetCursorPosY() - startY + ImGui.GetStyle().WindowPadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;
    }
}
