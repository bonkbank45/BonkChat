namespace ChatTwo.Ai;

public enum AiMode
{
    Grammar,
    Translate,
    /// <summary> Translate a received message into Thai for the reader. </summary>
    Explain,
    /// <summary> Rewrite the message in a different tone. </summary>
    Rewrite,
}

/// <summary>
/// A rewrite tone. Built-in and user-defined styles are the same type, so the
/// menus and the request path treat them identically.
/// </summary>
public readonly record struct AiStyle(string Name, string Instruction)
{
    private const string Rephrase =
        "Rewrite this differently: keep the same meaning and tone, but change the wording and sentence structure.";

    public static readonly AiStyle[] BuiltIn =
    [
        new("Rephrase", Rephrase),
        new("Politer", "Rewrite the message to be more polite and courteous, suitable for talking to strangers in an online game."),
        new("Friendlier", "Rewrite the message to be warmer, more friendly and casual, like chatting with close friends in an online game."),
        new("Shorter", "Rewrite the message to be as short and concise as possible while keeping its meaning and tone."),
    ];

    /// <summary>
    /// Politeness and friendliness mean nothing in the middle of a scene, so
    /// roleplay mode offers the adjustments that actually come up there:
    /// another take, length, and intensity.
    /// </summary>
    public static readonly AiStyle[] RoleplayBuiltIn =
    [
        new("Rephrase", Rephrase),
        // Both say so in words rather than trusting "condense" or "expand":
        // the tone rules push towards richer wording, and Shorter came back
        // longer than what it was given.
        // "Say more" reads as "describe more", and a short spoken line has
        // nothing to describe without inventing it, so the ways of getting
        // longer are named outright.
        new("Longer", "Make this noticeably longer than the input, but only by drawing out what it already says: "
                      + "emphasis, repetition, hesitation and the filler a person actually uses out loud, and richer "
                      + "phrasing of the same content. Never describe anything the message does not already describe, "
                      + "and never introduce new actions, participants or events."),
        new("Shorter", "Make this noticeably shorter than the input: keep only what matters, cut description that is "
                       + "not needed, and never return something as long as or longer than what you were given."),
        new("Bolder", "Make the wording noticeably more direct and intense than the input, swapping soft or vague "
                      + "words for blunt, unambiguous ones. Do not introduce new actions, thoughts or dialogue, "
                      + "and never return the input unchanged."),
    ];

    /// <summary> Built-in styles for the mode, followed by the user's own. </summary>
    public static IEnumerable<AiStyle> All(bool roleplay = false)
    {
        foreach (var style in roleplay ? RoleplayBuiltIn : BuiltIn)
            yield return style;

        foreach (var custom in Plugin.Config.AiCustomStyles)
            if (!string.IsNullOrWhiteSpace(custom.Name) && !string.IsNullOrWhiteSpace(custom.Instruction))
                yield return new AiStyle(custom.Name.Trim(), custom.Instruction.Trim());
    }
}

[Serializable]
public class AiCustomStyle
{
    public string Name = string.Empty;
    public string Instruction = string.Empty;

    public AiCustomStyle Clone() => new() { Name = Name, Instruction = Instruction };
}

/// <summary>
/// A pending AI result shown in the suggestion panel above the chat input,
/// waiting for the user to apply or dismiss it.
/// </summary>
public class AiSuggestion
{
    public required AiMode Mode;
    /// <summary> The full input text at the time of the request. </summary>
    public required string OriginalInput;
    /// <summary> Command prefix (e.g. "/say "), kept out of the AI request. </summary>
    public required string Prefix;
    public required string Corrected;
    /// <summary> The tone used, when Mode is Rewrite. </summary>
    public string? StyleName;
    public List<string> Explanations = [];
    /// <summary> Corrected text split into words, flagged when changed. </summary>
    public List<(string Word, bool Changed)> Words = [];

    /// <summary>
    /// Word-level LCS diff: returns the corrected text's words, marking the
    /// ones that don't appear (in order) in the original as changed.
    /// </summary>
    public static List<(string Word, bool Changed)> DiffWords(string original, string corrected)
    {
        var a = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var result = new List<(string, bool)>();
        var (x, y) = (0, 0);
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add((b[y], false));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                x++; // word removed from the original
            }
            else
            {
                result.Add((b[y], true));
                y++;
            }
        }

        for (; y < b.Length; y++)
            result.Add((b[y], true));

        return result;
    }
}
