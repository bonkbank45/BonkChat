using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace ChatTwo.Ui.ChatLog;

public partial class ChatLog
{
    public void DrawChannelName(Tab activeTab, bool sendChannelSwitch = false)
    {
        var currentChannel = ReadChannelName(activeTab);
        if (sendChannelSwitch && !currentChannel.SequenceEqual(PreviousChannel))
        {
            PreviousChannel = currentChannel;
            Plugin.ServerCore.SendChannelSwitch(currentChannel);
        }

        // Roleplay mode changes how everything you send is rewritten, so it
        // gets a marker that is visible without hunting for a button state.
        if (Plugin.Config.AiEnabled && activeTab.RoleplayMode)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedPink))
                ImGui.TextUnformatted("[RP] ");

            ImGui.SameLine();
        }

        InputHandler.ChunkHandler.DrawChunks(currentChannel);
    }
}