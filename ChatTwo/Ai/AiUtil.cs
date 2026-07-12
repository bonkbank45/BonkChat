namespace ChatTwo.Ai;

public static class AiUtil
{
    public static readonly string UserAgent =
        $"BonkChat/{typeof(AiUtil).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public static string Truncate(string text, int max = 300)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
