using System.Numerics;
using ChatTwo.Util;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace ChatTwo.Ui;

/// <summary>
/// Modal for cropping a background image. The crop rectangle is locked to the
/// aspect ratio of the user's current chat window, so what is selected here is
/// exactly what the window shows. Drag to move the region, slider to zoom.
/// </summary>
public static class BackgroundCropPopup
{
    private static float Zoom = 1f;
    private static Vector2 Center = new(0.5f, 0.5f);
    private static Vector4 InitialCrop = new(0, 0, 1, 1);
    private static bool PendingInit;

    public static void Open(string popupId, Vector4 currentCrop)
    {
        InitialCrop = currentCrop;
        PendingInit = true;
        ImGui.OpenPopup(popupId);
    }

    /// <summary> Draws the popup; returns true when a new crop was applied. </summary>
    public static bool Draw(string popupId, string imagePath, ref Vector4 crop)
    {
        using var popup = ImRaii.PopupModal(popupId, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
            return false;

        var texture = Plugin.TextureProvider.GetFromFile(imagePath).GetWrapOrDefault();
        if (texture == null || texture.Width == 0 || texture.Height == 0)
        {
            ImGui.TextUnformatted("Loading image...");
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            return false;
        }

        float imageWidth = texture.Width;
        float imageHeight = texture.Height;
        var imageAspect = imageWidth / imageHeight;

        // The crop is locked to the chat window's current aspect ratio.
        var windowSize = ChatLog.ChatLog.MainWindowSize;
        var targetAspect = windowSize.X > 0 && windowSize.Y > 0 ? windowSize.X / windowSize.Y : 2f;

        // The largest UV rect with the target aspect ratio (zoom = 1).
        float coverWidth, coverHeight;
        if (imageAspect > targetAspect)
        {
            coverHeight = 1f;
            coverWidth = targetAspect * imageHeight / imageWidth;
        }
        else
        {
            coverWidth = 1f;
            coverHeight = imageWidth / targetAspect / imageHeight;
        }

        if (PendingInit)
        {
            PendingInit = false;
            var initWidth = InitialCrop.Z - InitialCrop.X;
            var initHeight = InitialCrop.W - InitialCrop.Y;
            if (initWidth is > 0 and < 1 || initHeight is > 0 and < 1)
            {
                Center = new Vector2(InitialCrop.X + initWidth / 2, InitialCrop.Y + initHeight / 2);
                Zoom = Math.Clamp(initWidth > 0 ? coverWidth / initWidth : 1f, 1f, 4f);
            }
            else
            {
                Center = new Vector2(0.5f, 0.5f);
                Zoom = 1f;
            }
        }

        var uvWidth = coverWidth / Zoom;
        var uvHeight = coverHeight / Zoom;
        Center.X = Math.Clamp(Center.X, uvWidth / 2, 1 - uvWidth / 2);
        Center.Y = Math.Clamp(Center.Y, uvHeight / 2, 1 - uvHeight / 2);

        // Preview scaled to a comfortable size.
        var previewWidth = 500f * ImGuiHelpers.GlobalScale;
        var previewHeight = previewWidth / imageAspect;
        if (previewHeight > 350f * ImGuiHelpers.GlobalScale)
        {
            previewHeight = 350f * ImGuiHelpers.GlobalScale;
            previewWidth = previewHeight * imageAspect;
        }
        var previewSize = new Vector2(previewWidth, previewHeight);

        var origin = ImGui.GetCursorScreenPos();
        ImGui.Image(texture.Handle, previewSize);

        // Drag anywhere on the preview to move the crop region.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##crop-drag", previewSize);
        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            Center += new Vector2(delta.X / previewSize.X, delta.Y / previewSize.Y);
            Center.X = Math.Clamp(Center.X, uvWidth / 2, 1 - uvWidth / 2);
            Center.Y = Math.Clamp(Center.Y, uvHeight / 2, 1 - uvHeight / 2);
        }

        var uv0 = Center - new Vector2(uvWidth, uvHeight) / 2;
        var uv1 = Center + new Vector2(uvWidth, uvHeight) / 2;

        // Dim everything outside the crop rectangle.
        var drawList = ImGui.GetWindowDrawList();
        var rect0 = origin + uv0 * previewSize;
        var rect1 = origin + uv1 * previewSize;
        var end = origin + previewSize;
        const uint dim = 0xAA000000;
        drawList.AddRectFilled(origin, new Vector2(end.X, rect0.Y), dim); // top
        drawList.AddRectFilled(new Vector2(origin.X, rect1.Y), end, dim); // bottom
        drawList.AddRectFilled(new Vector2(origin.X, rect0.Y), new Vector2(rect0.X, rect1.Y), dim); // left
        drawList.AddRectFilled(new Vector2(rect1.X, rect0.Y), new Vector2(end.X, rect1.Y), dim); // right
        drawList.AddRect(rect0, rect1, 0xFFFFFFFF, 0f, ImDrawFlags.None, 2f);

        ImGui.SetNextItemWidth(previewSize.X - 100f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("Zoom", ref Zoom, 1f, 4f, "%.2fx");

        ImGuiUtil.HelpText($"Drag the image to position the crop. The frame matches your chat window ({windowSize.X:0}x{windowSize.Y:0}).");
        ImGui.Spacing();

        var applied = false;
        if (ImGui.Button("Apply"))
        {
            crop = new Vector4(uv0.X, uv0.Y, uv1.X, uv1.Y);
            applied = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset to full image"))
        {
            crop = new Vector4(0, 0, 1, 1);
            Center = new Vector2(0.5f, 0.5f);
            Zoom = 1f;
            applied = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        return applied;
    }
}
