using ChatTwo.Http;
using Dalamud.Interface;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace ChatTwo;

public class FontManager
{
    public IFontHandle Axis = null!;
    public IFontHandle AxisItalic = null!;

    public IFontHandle RegularFont = null!;
    public IFontHandle? ItalicFont;

    public IFontHandle FontAwesome = null!;

    public readonly byte[] GameSymFont;

    private ushort[] Ranges = [];
    private ushort[] JpRange = [];
    // Thai block U+0E00-U+0E7F, terminated per ImGui glyph range convention.
    private static readonly ushort[] ThaiRange = [0x0E01, 0x0E7F, 0];

    public static readonly HashSet<float> AxisFontSizeList =
    [
        9.6f, 10f, 12f, 14f, 16f,
        18f, 18.4f, 20f, 23f, 34f,
        36f, 40f, 45f, 46f, 68f, 90f,
    ];

    public FontManager()
    {
        var filePath = Path.Combine(Plugin.Interface.ConfigDirectory.FullName, "FFXIV_Lodestone_SSF.ttf");
        if (File.Exists(filePath))
        {
            GameSymFont = File.ReadAllBytes(filePath);
        }
        else
        {
            GameSymFont = ServerCore.HttpClient.GetAsync("https://img.finalfantasyxiv.com/lds/pc/global/fonts/FFXIV_Lodestone_SSF.ttf")
                .Result
                .Content
                .ReadAsByteArrayAsync()
                .Result;

            Dalamud.Utility.FilesystemUtil.WriteAllBytesSafe(filePath, GameSymFont);
        }
    }

    private unsafe void SetUpRanges()
    {
        ushort[] BuildRange(IReadOnlyList<ushort>? chars, params nint[] ranges)
        {
            var builder = new ImFontGlyphRangesBuilderPtr(ImGuiNative.ImFontGlyphRangesBuilder());
            // text
            foreach (var range in ranges)
                builder.AddRanges((ushort*)range);

            // chars
            if (chars != null)
            {
                for (var i = 0; i < chars.Count; i += 2)
                {
                    if (chars[i] == 0)
                        break;

                    for (var j = (uint) chars[i]; j <= chars[i + 1]; j++)
                        builder.AddChar((ushort) j);
                }
            }

            // Ingame supported ranges
            var reader = new FdtReader(Plugin.DataManager.GetFile("common/font/axis_12.fdt")!.Data);
            foreach (var c in reader.Glyphs)
                builder.AddChar(c.Char);

            // various symbols
            // French
            // Romanian
            // builder.AddText("←→↑↓《》■※☀★★☆♥♡ヅツッシ☀☁☂℃℉°♀♂♠♣♦♣♧®©™€$£♯♭♪✓√◎◆◇♦■□〇●△▽▼▲‹›≤≥<«“”─＼～");
            builder.AddText("Œœ");
            builder.AddText("ĂăÂâÎîȘșȚț");

            // "Enclosed Alphanumerics" (partial) https://www.compart.com/en/unicode/block/U+2460
            for (var i = 0x2460; i <= 0x24B5; i++)
                builder.AddChar((char) i);

            builder.AddChar('⓪');
            return builder.BuildRangesToArray();
        }

        var ranges = new List<nint> { (nint)ImGui.GetIO().Fonts.GetGlyphRangesDefault() };
        foreach (var extraRange in Enum.GetValues<ExtraGlyphRanges>())
            if (Plugin.Config.ExtraGlyphRanges.HasFlag(extraRange))
                ranges.Add(extraRange.Range());

        Ranges = BuildRange(null, ranges.ToArray());
        JpRange = BuildRange(GlyphRangesJapanese.GlyphRanges);
    }

    public void BuildFonts()
    {
        SetUpRanges();

        Axis = Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, SizeInPx(Plugin.Config.FontSizeV2)));
        AxisItalic = Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, SizeInPx(Plugin.Config.FontSizeV2))
        {
            SkewStrength = SizeInPx(Plugin.Config.FontSizeV2) / 6
        });

        FontAwesome = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = GetFontSize() }));
            e.OnPostBuild(tk => tk.FitRatio(tk.Font));
        });

        RegularFont = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(
                tk =>
                {
                    var config = new SafeFontConfig {SizePt = Plugin.Config.GlobalFontV2.SizePt, GlyphRanges = Ranges};
                    config.MergeFont = Plugin.Config.GlobalFontV2.FontId.AddToBuildToolkit(tk, config);

                    config.SizePt = Plugin.Config.JapaneseFontV2.SizePt;
                    config.GlyphRanges = JpRange;
                    Plugin.Config.JapaneseFontV2.FontId.AddToBuildToolkit(tk, config);

                    AddThaiFont(tk, config);

                    config.SizePt = Plugin.Config.SymbolsFontSizeV2;
                    tk.AddGameSymbol(config);

                    tk.Font = config.MergeFont;
                }
            ));

        if (Plugin.Config.ItalicEnabled)
        {
            ItalicFont = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(
                e => e.OnPreBuild(
                    tk =>
                    {
                        var config = new SafeFontConfig {SizePt = Plugin.Config.ItalicFontV2.SizePt, GlyphRanges = Ranges};
                        config.MergeFont = Plugin.Config.ItalicFontV2.FontId.AddToBuildToolkit(tk, config);

                        config.SizePt = Plugin.Config.JapaneseFontV2.SizePt;
                        config.GlyphRanges = JpRange;
                        Plugin.Config.JapaneseFontV2.FontId.AddToBuildToolkit(tk, config);

                        config.SizePt = Plugin.Config.SymbolsFontSizeV2;
                        tk.AddGameSymbol(config);

                        tk.Font = config.MergeFont;
                    }
                ));
        }
        else
        {
            ItalicFont = null;
        }
    }

    /// <summary>
    /// Merges a Thai-capable system font for the AI learning features (Thai
    /// explanations and Thai input for translation). The game fonts have no
    /// Thai glyphs, so without this Thai text renders as boxes.
    /// </summary>
    private static void AddThaiFont(IFontAtlasBuildToolkitPreBuild tk, SafeFontConfig config)
    {
        if (!Plugin.Config.AiEnabled)
            return;

        try
        {
            var families = IFontFamilyId.ListSystemFonts(false);
            var family = families.FirstOrDefault(f => f.EnglishName == "Leelawadee UI")
                         ?? families.FirstOrDefault(f => f.EnglishName == "Tahoma");
            if (family == null || family.Fonts.Count == 0)
            {
                Plugin.Log.Warning("No Thai-capable system font found; Thai text will not render");
                return;
            }

            config.SizePt = Plugin.Config.GlobalFontV2.SizePt;
            config.GlyphRanges = ThaiRange;
            family.Fonts[0].AddToBuildToolkit(tk, config);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to add a Thai font, Thai text will not render");
        }
    }

    public static float SizeInPt(float px) => (float) (px * 3.0 / 4.0);
    public static float SizeInPx(float pt) => (float) (pt * 4.0 / 3.0);
    public static float GetFontSize() => Plugin.Config.FontsEnabled ? Plugin.Config.GlobalFontV2.SizePx : SizeInPx(Plugin.Config.FontSizeV2);
}
