namespace ChatTwo.Ai;

public static class AiUtil
{
    public static string Truncate(string text, int max = 300)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
