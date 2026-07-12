using System.Collections;
using System.Numerics;
using ChatTwo.Code;
using ChatTwo.GameFunctions.Types;
using ChatTwo.Resources;
using ChatTwo.Util;
using Dalamud;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Bindings.ImGui;
using Lumina.Text.ReadOnly;

namespace ChatTwo;

[Serializable]
public class ConfigKeyBind
{
    public ModifierFlag Modifier;
    public VirtualKey Key;

    public override string ToString()
    {
        var modString = "";
        if (Modifier.HasFlag(ModifierFlag.Ctrl))
            modString += Language.Keybind_Modifier_Ctrl + " + ";
        if (Modifier.HasFlag(ModifierFlag.Shift))
            modString += Language.Keybind_Modifier_Shift + " + ";
        if (Modifier.HasFlag(ModifierFlag.Alt))
            modString += Language.Keybind_Modifier_Alt + " + ";
        return modString+Key.GetFancyName();
    }
}

[Serializable]
public enum MigrationStatus
{
    NotStarted,
    Started,
    Copied,
    Failed,
    Finished,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    private const int LatestVersion = 6;

    public int Version { get; set; } = LatestVersion;

    public bool HideChat = true;
    public bool HideDuringCutscenes = true;
    public bool HideWhenNotLoggedIn = true;
    public bool HideWhenUiHidden = true;
    public bool HideInLoadingScreens;
    public bool HideInBattle;
    public bool HideWhenInactive;
    public int InactivityHideTimeout = 10;
    public bool InactivityHideActiveDuringBattle = true;

    [Obsolete("Use InactivityHideChannelsV2 instead")]
    public Dictionary<ChatType, ChatSource> InactivityHideChannels = [];

    public Dictionary<ChatType, (ChatSource, ChatSource)> InactivityHideChannelsV2 = [];
    public bool InactivityHideExtraChatAll = true;
    public HashSet<Guid> InactivityHideExtraChatChannels = [];
    public bool ShowHideButton = true;
    public bool NativeItemTooltips = true;
    public bool PrettierTimestamps = true;
    public bool MoreCompactPretty;
    public bool HideSameTimestamps;
    public bool ShowNoviceNetwork;
    public bool SidebarTabView;
    public bool PrintChangelog = true;
    public bool OnlyPreviewIf;
    public int PreviewMinimum = 1;
    public PreviewPosition PreviewPosition = PreviewPosition.Inside;
    public CommandHelpSide CommandHelpSide = CommandHelpSide.None;
    public KeybindMode KeybindMode = KeybindMode.Strict;
    public LanguageOverride LanguageOverride = LanguageOverride.None;
    public bool CanMove = true;
    public bool CanResize = true;
    public bool ShowTitleBar;
    public bool ShowPopOutTitleBar = true;
    public bool DatabaseBattleMessages;
    public bool DatabaseGatherCraftMessages;
    public bool LoadPreviousSession;
    public bool FilterIncludePreviousSessions;
    public bool SortAutoTranslate;
    public bool CollapseDuplicateMessages;
    public bool CollapseKeepUniqueLinks;
    public bool PlaySounds = true;
    public bool KeepInputFocus = true;
    public int MaxLinesToRender = 10_000; // 1-10000
    public bool Use24HourClock;

    public bool ShowEmotes = true;
    public HashSet<string> BlockedEmotes = [];

    public bool FontsEnabled = true;
    public ExtraGlyphRanges ExtraGlyphRanges = 0;
    public float FontSizeV2 = 12.75f;
    public float SymbolsFontSizeV2 = 12.75f;
    public SingleFontSpec GlobalFontV2 = new()
    {
        // dalamud only ships KR as regular, which chat2 used previously for global fonts
        FontId = new DalamudAssetFontAndFamilyId(DalamudAsset.NotoSansCjkRegular),
        SizePt = 12.75f,
    };
    public SingleFontSpec JapaneseFontV2 = new()
    {
        FontId = new DalamudAssetFontAndFamilyId(DalamudAsset.NotoSansCjkMedium),
        SizePt = 12.75f,
    };
    public bool ItalicEnabled;
    public SingleFontSpec ItalicFontV2 = new()
    {
        FontId = new DalamudAssetFontAndFamilyId(DalamudAsset.NotoSansCjkRegular),
        SizePt = 12.75f,
    };

    public float TooltipOffset;
    public float WindowAlpha = 100f;
    public Dictionary<ChatType, uint> ChatColours = new();
    public List<Tab> Tabs = [];

    public bool OverrideStyle;
    public string? ChosenStyle;

    public ConfigKeyBind? ChatTabForward;
    public ConfigKeyBind? ChatTabBackward;

    // AI
    public bool AiEnabled;
    public Ai.AiProviderType AiProvider = Ai.AiProviderType.SwuAi;
    public string OpenAiApiKey = string.Empty;
    public string OpenAiModel = "gpt-4o-mini";
    public string GeminiApiKey = string.Empty;
    public string GeminiModel = "gemini-2.5-flash";
    public string SwuAiApiKey = string.Empty;
    public string SwuAiUserId = string.Empty;
    public string SwuAiModel = "google/gemini-2.5-flash";
    public string AiGrammarPrompt = DefaultGrammarPrompt;
    public string AiTranslatePrompt = DefaultTranslatePrompt;
    public string AiExplainPrompt = DefaultExplainPrompt;
    public ConfigKeyBind? AiGrammarKeybind = new() { Modifier = ModifierFlag.Ctrl, Key = VirtualKey.G };
    public ConfigKeyBind? AiTranslateKeybind = new() { Modifier = ModifierFlag.Ctrl, Key = VirtualKey.T };

    // The pre-1.41.0 default, kept so it can be upgraded in place on load.
    public const string LegacyGrammarPrompt =
        "You are an English grammar assistant for a video game chat. "
        + "Correct the grammar and spelling of the user's message while keeping its meaning, tone and casual style. "
        + "Reply with ONLY the corrected message - no quotes, no explanations, no extra text. "
        + "If the message is already correct, reply with the original message unchanged.";

    public const string DefaultGrammarPrompt =
        "You are an English teacher helping a Thai player practice English in an online game chat. "
        + "Correct the grammar and spelling of the user's message while keeping its meaning, tone and casual style. "
        + "Reply with ONLY minified JSON in exactly this shape, no markdown, no extra text: "
        + "{\"corrected\":\"<the corrected message>\",\"explanations\":[\"<short explanation in Thai>\"]} "
        + "Each explanation is one short Thai sentence about one fix, quoting the relevant English words. "
        + "If the message is already correct, return it unchanged with an empty explanations array.";

    public const string DefaultExplainPrompt =
        "You are an English teacher helping a Thai player understand English messages in an online game. "
        + "Translate the message into natural Thai. "
        + "Reply with ONLY minified JSON in exactly this shape, no markdown, no extra text: "
        + "{\"corrected\":\"<Thai translation>\",\"explanations\":[\"<short Thai note>\"]} "
        + "Explanations: up to 3 short Thai notes explaining slang, gaming abbreviations or useful vocabulary from the message.";

    public const string DefaultTranslatePrompt =
        "You are an English teacher helping a Thai player chat in an online game. "
        + "Translate the user's Thai (or mixed Thai-English) message into natural, casual English suitable for game chat. "
        + "Reply with ONLY minified JSON in exactly this shape, no markdown, no extra text: "
        + "{\"corrected\":\"<the English message>\",\"explanations\":[\"<short note in Thai>\"]} "
        + "Explanations: up to 3 short Thai notes teaching vocabulary or phrasing used in the translation.";

    // Background image
    public string BackgroundImagePath = string.Empty;
    public float BackgroundImageOpacity = 50f; // percent
    public BackgroundImageFit BackgroundImageFitMode = BackgroundImageFit.Cover;
    /// <summary> Crop region as UV rect (X,Y = top-left, Z,W = bottom-right); (0,0,1,1) = full image. </summary>
    public Vector4 BackgroundImageCrop = new(0, 0, 1, 1);

    // Webinterface
    public bool WebinterfaceEnabled;
    public bool WebinterfaceAutoStart;
    public string WebinterfacePassword = WebinterfaceUtil.GenerateSimpleAuthCode();
    public int WebinterfacePort = 9000;
    public HashSet<string> AuthStore = [];
    public int WebinterfaceMaxLinesToSend = 1000; // 1-10000

    // Migration safety
    public MigrationStatus MigrationStatus = MigrationStatus.NotStarted;

    public void UpdateFrom(Configuration other, bool backToOriginal)
    {
        if (backToOriginal)
            foreach (var tab in Tabs.Where(t => t.PopOut))
                tab.PopOut = false;

        HideChat = other.HideChat;
        HideDuringCutscenes = other.HideDuringCutscenes;
        HideWhenNotLoggedIn = other.HideWhenNotLoggedIn;
        HideWhenUiHidden = other.HideWhenUiHidden;
        HideInLoadingScreens = other.HideInLoadingScreens;
        HideInBattle = other.HideInBattle;
        HideWhenInactive = other.HideWhenInactive;
        InactivityHideTimeout = other.InactivityHideTimeout;
        InactivityHideActiveDuringBattle = other.InactivityHideActiveDuringBattle;
        InactivityHideChannelsV2 = other.InactivityHideChannelsV2.ToDictionary(pair => pair.Key, pair => pair.Value);
        InactivityHideExtraChatAll = other.InactivityHideExtraChatAll;
        InactivityHideExtraChatChannels = other.InactivityHideExtraChatChannels.ToHashSet();
        ShowHideButton = other.ShowHideButton;
        NativeItemTooltips = other.NativeItemTooltips;
        PrettierTimestamps = other.PrettierTimestamps;
        MoreCompactPretty = other.MoreCompactPretty;
        HideSameTimestamps = other.HideSameTimestamps;
        ShowNoviceNetwork = other.ShowNoviceNetwork;
        SidebarTabView = other.SidebarTabView;
        PrintChangelog = other.PrintChangelog;
        OnlyPreviewIf = other.OnlyPreviewIf;
        PreviewMinimum = other.PreviewMinimum;
        PreviewPosition = other.PreviewPosition;
        CommandHelpSide = other.CommandHelpSide;
        KeybindMode = other.KeybindMode;
        LanguageOverride = other.LanguageOverride;
        CanMove = other.CanMove;
        CanResize = other.CanResize;
        ShowTitleBar = other.ShowTitleBar;
        ShowPopOutTitleBar = other.ShowPopOutTitleBar;
        DatabaseBattleMessages = other.DatabaseBattleMessages;
        DatabaseGatherCraftMessages = other.DatabaseGatherCraftMessages;
        LoadPreviousSession = other.LoadPreviousSession;
        FilterIncludePreviousSessions = other.FilterIncludePreviousSessions;
        SortAutoTranslate = other.SortAutoTranslate;
        CollapseDuplicateMessages = other.CollapseDuplicateMessages;
        CollapseKeepUniqueLinks = other.CollapseKeepUniqueLinks;
        PlaySounds = other.PlaySounds;
        KeepInputFocus = other.KeepInputFocus;
        MaxLinesToRender = other.MaxLinesToRender;
        Use24HourClock = other.Use24HourClock;
        ShowEmotes = other.ShowEmotes;
        BlockedEmotes = other.BlockedEmotes;
        FontsEnabled = other.FontsEnabled;
        ItalicEnabled = other.ItalicEnabled;
        ExtraGlyphRanges = other.ExtraGlyphRanges;
        FontSizeV2 = other.FontSizeV2;
        GlobalFontV2 = other.GlobalFontV2;
        JapaneseFontV2 = other.JapaneseFontV2;
        ItalicFontV2 = other.ItalicFontV2;
        SymbolsFontSizeV2 = other.SymbolsFontSizeV2;
        TooltipOffset = other.TooltipOffset;
        WindowAlpha = other.WindowAlpha;
        ChatColours = other.ChatColours.ToDictionary(entry => entry.Key, entry => entry.Value);
        Tabs = other.Tabs.Select(t => t.Clone()).ToList();
        OverrideStyle = other.OverrideStyle;
        ChosenStyle = other.ChosenStyle;
        ChatTabForward = other.ChatTabForward;
        ChatTabBackward = other.ChatTabBackward;
        AiEnabled = other.AiEnabled;
        AiProvider = other.AiProvider;
        OpenAiApiKey = Ai.SecretUtil.Seal(other.OpenAiApiKey);
        OpenAiModel = other.OpenAiModel;
        GeminiApiKey = Ai.SecretUtil.Seal(other.GeminiApiKey);
        GeminiModel = other.GeminiModel;
        SwuAiApiKey = Ai.SecretUtil.Seal(other.SwuAiApiKey);
        SwuAiUserId = other.SwuAiUserId;
        SwuAiModel = other.SwuAiModel;
        AiGrammarPrompt = other.AiGrammarPrompt;
        AiTranslatePrompt = other.AiTranslatePrompt;
        AiExplainPrompt = other.AiExplainPrompt;
        AiGrammarKeybind = other.AiGrammarKeybind;
        AiTranslateKeybind = other.AiTranslateKeybind;
        BackgroundImagePath = other.BackgroundImagePath;
        BackgroundImageOpacity = other.BackgroundImageOpacity;
        BackgroundImageFitMode = other.BackgroundImageFitMode;
        BackgroundImageCrop = other.BackgroundImageCrop;
        WebinterfaceEnabled = other.WebinterfaceEnabled;
        WebinterfaceAutoStart = other.WebinterfaceAutoStart;
        WebinterfacePassword = other.WebinterfacePassword;
        WebinterfacePort = other.WebinterfacePort;
        WebinterfaceMaxLinesToSend = other.WebinterfaceMaxLinesToSend;
        MigrationStatus = other.MigrationStatus;
    }
}

[Serializable]
public enum BackgroundImageFit
{
    /// <summary> Stretch to the window, ignoring aspect ratio. </summary>
    Stretch,
    /// <summary> Fill the window, cropping the overflowing sides. </summary>
    Cover,
}

[Serializable]
public enum UnreadMode
{
    All,
    Unseen,
    None,
}

public static class UnreadModeExt
{
    public static string Name(this UnreadMode mode) => mode switch
    {
        UnreadMode.All => Language.UnreadMode_All,
        UnreadMode.Unseen => Language.UnreadMode_Unseen,
        UnreadMode.None => Language.UnreadMode_None,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string? Tooltip(this UnreadMode mode) => mode switch
    {
        UnreadMode.All => Language.UnreadMode_All_Tooltip,
        UnreadMode.Unseen => Language.UnreadMode_Unseen_Tooltip,
        UnreadMode.None => Language.UnreadMode_None_Tooltip,
        _ => null,
    };
}

[Serializable]
public class Tab
{
    public string Name = Language.Tab_DefaultName;

    [Obsolete("Removed in favor of SelectedChannels")]
    public Dictionary<ChatType, ChatSource> ChatCodes = new();

    public Dictionary<ChatType, (ChatSource, ChatSource)> SelectedChannels = new();
    public bool ExtraChatAll;
    public HashSet<Guid> ExtraChatChannels = [];

    public UnreadMode UnreadMode = UnreadMode.Unseen;
    public bool UnhideOnActivity;
    public bool DisplayTimestamp = true;
    public InputChannel? Channel;
    public bool PopOut;
    public bool IndependentOpacity;
    public float Opacity = 100f;
    public bool InputDisabled;
    public bool SupportsInput;

    /// <summary> Per-tab background image; empty falls back to the global one. </summary>
    public string BackgroundImagePath = string.Empty;
    /// <summary> Crop region for the tab image as UV rect; (0,0,1,1) = full image. </summary>
    public Vector4 BackgroundImageCrop = new(0, 0, 1, 1);

    public bool CanMove = true;
    public bool CanResize = true;

    public bool IndependentHide;
    public bool HideDuringCutscenes = true;
    public bool HideWhenNotLoggedIn = true;
    public bool HideWhenUiHidden = true;
    public bool HideInLoadingScreens;
    public bool HideInBattle;
    public bool HideWhenInactive;

    public bool IsTempTab;
    public bool AllSenderMessages;
    public TellTarget TellTarget = TellTarget.Empty();

    [NonSerialized] public uint Unread;
    [NonSerialized] public uint LastSendUnread;
    [NonSerialized] public long LastActivity;
    [NonSerialized] public MessageList Messages = new();

    [NonSerialized] public UsedChannel CurrentChannel = new();

    [NonSerialized] public Guid Identifier = Guid.NewGuid();

    public bool Matches(Message message)
    {
        if (Channel == InputChannel.Tell && TellTarget.IsSet())
        {
            if (!message.Code.IsPlayerMessage())
                return false;

            if (TellTarget.ContentId == 0)
            {
                var target = TellTarget.Empty();
                foreach (var payload in new ReadOnlySeString(message.SenderSource.Encode()))
                {
                    if (target.FromCharacterLink(payload))
                        break; // Character link found
                }

                if (target.CompareNames(TellTarget))
                    TellTarget.ContentId = message.ContentId;
            }

            return message.MatchTellTarget(TellTarget, AllSenderMessages);
        }

        return message.Matches(SelectedChannels, ExtraChatAll, ExtraChatChannels);
    }

    public void AddMessage(Message message, bool unread = true)
    {
        Messages.AddPrune(message, MessageManager.MessageDisplayLimit);
        if (!unread)
            return;

        Unread += 1;
        if (message.Matches(Plugin.Config.InactivityHideChannelsV2, Plugin.Config.InactivityHideExtraChatAll, Plugin.Config.InactivityHideExtraChatChannels))
            LastActivity = Environment.TickCount64;
    }

    public void Clear()
        => Messages.Clear();

    public Tab Clone()
    {
        return new Tab
        {
            Name = Name,
            SelectedChannels = SelectedChannels.ToDictionary(pair => pair.Key, pair => pair.Value),
            ExtraChatAll = ExtraChatAll,
            ExtraChatChannels = ExtraChatChannels.ToHashSet(),
            UnreadMode = UnreadMode,
            UnhideOnActivity = UnhideOnActivity,
            Unread = Unread,
            LastActivity = LastActivity,
            DisplayTimestamp = DisplayTimestamp,
            Channel = Channel,
            PopOut = PopOut,
            IndependentOpacity = IndependentOpacity,
            Opacity = Opacity,
            Identifier = Identifier,
            InputDisabled = InputDisabled,
            SupportsInput = SupportsInput,
            CurrentChannel = CurrentChannel.Clone(),
            CanMove = CanMove,
            CanResize = CanResize,
            IndependentHide = IndependentHide,
            HideDuringCutscenes = HideDuringCutscenes,
            HideWhenNotLoggedIn = HideWhenNotLoggedIn,
            HideWhenUiHidden = HideWhenUiHidden,
            HideInLoadingScreens = HideInLoadingScreens,
            HideInBattle = HideInBattle,
            HideWhenInactive = HideWhenInactive,
            IsTempTab = IsTempTab,
            AllSenderMessages = AllSenderMessages,
            TellTarget = TellTarget.Clone(),
            BackgroundImagePath = BackgroundImagePath,
            BackgroundImageCrop = BackgroundImageCrop,
        };
    }

    /// <summary>
    /// MessageList provides an ordered list of messages with duplicate ID
    /// tracking, sorting and mutex protection.
    /// </summary>
    public class MessageList
    {
        private readonly SemaphoreSlim LockSlim = new(1, 1);

        private readonly List<Message> Messages;
        private readonly HashSet<Guid> TrackedMessageIds;

        public MessageList()
        {
            Messages = [];
            TrackedMessageIds = [];
        }

        public MessageList(int initialCapacity)
        {
            Messages = new List<Message>(initialCapacity);
            TrackedMessageIds = new HashSet<Guid>(initialCapacity);
        }

        public void AddPrune(Message message, int max)
        {
            LockSlim.Wait(-1);
            try
            {
                AddLocked(message);
                PruneMaxLocked(max);
            }
            finally
            {
                LockSlim.Release();
            }
        }

        public void AddSortPrune(IEnumerable<Message> messages, int max)
        {
            LockSlim.Wait(-1);
            try
            {
                foreach (var message in messages)
                    AddLocked(message);

                SortLocked();
                PruneMaxLocked(max);
            }
            finally
            {
                LockSlim.Release();
            }
        }

        private void AddLocked(Message message)
        {
            if (TrackedMessageIds.Contains(message.Id))
                return;

            Messages.Add(message);
            TrackedMessageIds.Add(message.Id);
        }

        public void Clear()
        {
            LockSlim.Wait(-1);
            try
            {
                Messages.Clear();
                TrackedMessageIds.Clear();
            }
            finally
            {
                LockSlim.Release();
            }
        }

        private void SortLocked()
        {
            Messages.Sort((a, b) => a.Date.CompareTo(b.Date));
        }

        private void PruneMaxLocked(int max)
        {
            while (Messages.Count > max)
            {
                TrackedMessageIds.Remove(Messages[0].Id);
                Messages.RemoveAt(0);
            }
        }

        /// <summary>
        /// Returns an array copy of the message list for usage outside of main thread
        /// </summary>
        public async Task<Message[]> GetCopy(int millisecondsTimeout = -1)
        {
            await LockSlim.WaitAsync(millisecondsTimeout);
            try
            {
                return Messages.ToArray();
            }
            finally
            {
                LockSlim.Release();
            }
        }

        /// <summary>
        /// GetReadOnly returns a read-only list of messages while holding a
        /// reader lock. The list should be used with a using statement.
        /// </summary>
        public RLockedMessageList GetReadOnly(int millisecondsTimeout = -1)
        {
            LockSlim.Wait(millisecondsTimeout);
            return new RLockedMessageList(LockSlim, Messages);
        }

        public class RLockedMessageList(SemaphoreSlim lockSlim, List<Message> messages) : IReadOnlyList<Message>, IDisposable
        {
            public IEnumerator<Message> GetEnumerator()
            {
                return messages.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public int Count => messages.Count;

            public Message this[int index] => messages[index];

            public void Dispose()
            {
                lockSlim.Release();
            }
        }
    }
}

public class UsedChannel
{
    public InputChannel Channel = InputChannel.Invalid;
    public List<Chunk> Name = [];
    public TellTarget? TellTarget;

    public bool UseTempChannel;
    public InputChannel TempChannel = InputChannel.Invalid;
    public TellTarget? TempTellTarget;

    public void ResetTempChannel()
    {
        UseTempChannel = false;
        TempTellTarget = null;
        TempChannel = InputChannel.Invalid;
    }

    public void SetChannel(InputChannel channel)
    {
        Channel = channel;
        Name = [];
    }

    public UsedChannel Clone()
    {
        return new UsedChannel
        {
            Channel = Channel,
            Name = Name,
            TellTarget = TellTarget?.Clone(),

            UseTempChannel = UseTempChannel,
            TempChannel = TempChannel,
            TempTellTarget = TempTellTarget?.Clone(),
        };
    }
}

[Serializable]
public enum PreviewPosition
{
    None,
    Inside,
    Top,
    Bottom,
    Tooltip,
}

public static class PreviewPositionExt
{
    public static string Name(this PreviewPosition position) => position switch
    {
        PreviewPosition.None => Language.Options_Preview_None,
        PreviewPosition.Inside => Language.Options_Preview_Inside,
        PreviewPosition.Top => Language.Options_Preview_Top,
        PreviewPosition.Bottom => Language.Options_Preview_Bottom,
        PreviewPosition.Tooltip => Language.Options_Preview_Tooltip,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };
}

[Serializable]
public enum CommandHelpSide
{
    None,
    Left,
    Right,
}

public static class CommandHelpSideExt
{
    public static string Name(this CommandHelpSide side) => side switch
    {
        CommandHelpSide.None => Language.CommandHelpSide_None,
        CommandHelpSide.Left => Language.CommandHelpSide_Left,
        CommandHelpSide.Right => Language.CommandHelpSide_Right,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };
}

[Serializable]
public enum KeybindMode
{
    Flexible,
    Strict,
}

public static class KeybindModeExt
{
    public static string Name(this KeybindMode mode) => mode switch
    {
        KeybindMode.Flexible => Language.KeybindMode_Flexible_Name,
        KeybindMode.Strict => Language.KeybindMode_Strict_Name,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string? Tooltip(this KeybindMode mode) => mode switch
    {
        KeybindMode.Flexible => Language.KeybindMode_Flexible_Tooltip,
        KeybindMode.Strict => Language.KeybindMode_Strict_Tooltip,
        _ => null,
    };
}

[Serializable]
public enum LanguageOverride
{
    None,
    ChineseSimplified,
    ChineseTraditional,
    Dutch,
    English,
    French,
    German,
    Greek,

    // Italian,
    Japanese,

    // Korean,
    // Norwegian,
    PortugueseBrazil,
    Romanian,
    Russian,
    Spanish,
    Swedish,
}

public static class LanguageOverrideExt
{
    public static string Name(this LanguageOverride mode) => mode switch
    {
        LanguageOverride.None => Language.LanguageOverride_None,
        LanguageOverride.ChineseSimplified => "简体中文",
        LanguageOverride.ChineseTraditional => "繁體中文",
        LanguageOverride.Dutch => "Nederlands",
        LanguageOverride.English => "English",
        LanguageOverride.French => "Français",
        LanguageOverride.German => "Deutsch",
        LanguageOverride.Greek => "Ελληνικά",
        // LanguageOverride.Italian => "Italiano",
        LanguageOverride.Japanese => "日本語",
        // LanguageOverride.Korean => "한국어 (Korean)",
        // LanguageOverride.Norwegian => "Norsk",
        LanguageOverride.PortugueseBrazil => "Português do Brasil",
        LanguageOverride.Romanian => "Română",
        LanguageOverride.Russian => "Русский",
        LanguageOverride.Spanish => "Español",
        LanguageOverride.Swedish => "Svenska",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string Code(this LanguageOverride mode) => mode switch
    {
        LanguageOverride.None => "",
        LanguageOverride.ChineseSimplified => "zh-hans",
        LanguageOverride.ChineseTraditional => "zh-hant",
        LanguageOverride.Dutch => "nl",
        LanguageOverride.English => "en",
        LanguageOverride.French => "fr",
        LanguageOverride.German => "de",
        LanguageOverride.Greek => "el",
        // LanguageOverride.Italian => "it",
        LanguageOverride.Japanese => "ja",
        // LanguageOverride.Korean => "ko",
        // LanguageOverride.Norwegian => "no",
        LanguageOverride.PortugueseBrazil => "pt-br",
        LanguageOverride.Romanian => "ro",
        LanguageOverride.Russian => "ru",
        LanguageOverride.Spanish => "es",
        LanguageOverride.Swedish => "sv",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}

[Serializable]
[Flags]
public enum ExtraGlyphRanges
{
    ChineseFull = 1 << 0,
    ChineseSimplifiedCommon = 1 << 1,
    Cyrillic = 1 << 2,
    Japanese = 1 << 3,
    Korean = 1 << 4,
    Thai = 1 << 5,
    Vietnamese = 1 << 6,
}

public static class ExtraGlyphRangesExt
{
    public static string Name(this ExtraGlyphRanges ranges) => ranges switch
    {
        ExtraGlyphRanges.ChineseFull => Language.ExtraGlyphRanges_ChineseFull_Name,
        ExtraGlyphRanges.ChineseSimplifiedCommon => Language.ExtraGlyphRanges_ChineseSimplifiedCommon_Name,
        ExtraGlyphRanges.Cyrillic => Language.ExtraGlyphRanges_Cyrillic_Name,
        ExtraGlyphRanges.Japanese => Language.ExtraGlyphRanges_Japanese_Name,
        ExtraGlyphRanges.Korean => Language.ExtraGlyphRanges_Korean_Name,
        ExtraGlyphRanges.Thai => Language.ExtraGlyphRanges_Thai_Name,
        ExtraGlyphRanges.Vietnamese => Language.ExtraGlyphRanges_Vietnamese_Name,
        _ => throw new ArgumentOutOfRangeException(nameof(ranges), ranges, null),
    };

    public static unsafe nint Range(this ExtraGlyphRanges ranges) => ranges switch
    {
        ExtraGlyphRanges.ChineseFull => (nint)ImGui.GetIO().Fonts.GetGlyphRangesChineseFull(),
        ExtraGlyphRanges.ChineseSimplifiedCommon => (nint)ImGui.GetIO().Fonts.GetGlyphRangesChineseSimplifiedCommon(),
        ExtraGlyphRanges.Cyrillic => (nint)ImGui.GetIO().Fonts.GetGlyphRangesCyrillic(),
        ExtraGlyphRanges.Japanese => (nint)ImGui.GetIO().Fonts.GetGlyphRangesJapanese(),
        ExtraGlyphRanges.Korean => (nint)ImGui.GetIO().Fonts.GetGlyphRangesKorean(),
        ExtraGlyphRanges.Thai => (nint)ImGui.GetIO().Fonts.GetGlyphRangesThai(),
        ExtraGlyphRanges.Vietnamese => (nint)ImGui.GetIO().Fonts.GetGlyphRangesVietnamese(),
        _ => throw new ArgumentOutOfRangeException(nameof(ranges), ranges, null),
    };
}
