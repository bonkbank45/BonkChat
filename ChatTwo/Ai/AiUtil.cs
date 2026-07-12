namespace ChatTwo.Ai;

public static class AiUtil
{
    public static readonly string UserAgent =
        $"BonkChat/{typeof(AiUtil).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    // The AI providers use the WinHTTP stack instead of .NET's own
    // SocketsHttpHandler because Cloudflare in front of swuai.swu.ac.th
    // rejects the .NET TLS fingerprint with a 403 challenge page, while
    // WinHTTP requests go through. Disposed by AiManager.
    public static readonly HttpClient HttpClient = new(new System.Net.Http.WinHttpHandler());

    public static string Truncate(string text, int max = 300)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
