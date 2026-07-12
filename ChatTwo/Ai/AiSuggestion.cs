namespace ChatTwo.Ai;

public enum AiMode
{
    Grammar,
    Translate,
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
