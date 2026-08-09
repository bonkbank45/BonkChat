using System.Numerics;
using ChatTwo.GameFunctions.Types;

namespace ChatTwo.Ui;

public interface IChatWindow
{
    Vector2 LastWindowPos { get; set; }
    Vector2 LastWindowSize { get; set; }
    HideState CurrentHideState { get; set; }

    /// <summary>
    /// The tab this window is showing. A pop-out is fixed to its own tab, so
    /// anything scoped to a conversation (such as the AI scene context) must
    /// ask the window rather than assume the main window's current tab.
    /// </summary>
    Tab CurrentTab { get; }
}