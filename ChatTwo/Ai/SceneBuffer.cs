namespace ChatTwo.Ai;

/// <summary>
/// An append-only transcript of a conversation, used as context for AI
/// requests. Append-only is a hard requirement, not a style choice: xAI's
/// prompt caching only reuses a prefix that matches exactly, so editing,
/// reordering or dropping earlier lines would break the cache and make every
/// later request pay full price. When the buffer outgrows its budget it is
/// reset wholesale (with a short tail kept for continuity) under a fresh
/// conversation id instead of trimming from the front.
/// </summary>
public class SceneBuffer
{
    private const int MaxLineChars = 200;
    private const int SeedLinesOnReset = 2;

    private readonly Lock Mutex = new();
    private readonly List<string> Lines = [];

    /// <summary> Sent as x-grok-conv-id so xAI routes us to our own cache. </summary>
    public string ConversationId { get; private set; } = NewConversationId();

    private int EstimatedTokens;
    private long LastActivity = Environment.TickCount64;

    public int LineCount
    {
        get
        {
            lock (Mutex)
                return Lines.Count;
        }
    }

    public int TokenEstimate
    {
        get
        {
            lock (Mutex)
                return EstimatedTokens;
        }
    }

    private static string NewConversationId() => $"conv_{Guid.NewGuid():N}";

    /// <summary> Rough token estimate; only used to decide when to reset. </summary>
    private static int EstimateTokens(string text) => text.Length * 2 / 7;

    public void Append(string sender, string content)
    {
        content = content.ReplaceLineEndings(" ").Trim();
        if (content.Length == 0)
            return;

        if (content.Length > MaxLineChars)
            content = content[..MaxLineChars] + "…";

        var line = string.IsNullOrWhiteSpace(sender) ? content : $"{sender}: {content}";

        lock (Mutex)
        {
            ExpireIfIdle();

            // Overflowing rewinds the whole scene rather than dropping the
            // oldest lines, which would invalidate the cached prefix anyway.
            if (EstimatedTokens + EstimateTokens(line) > Math.Max(200, Plugin.Config.AiContextMaxTokens))
                ResetLocked(keepTail: true);

            Lines.Add(line);
            EstimatedTokens += EstimateTokens(line);
            LastActivity = Environment.TickCount64;
        }
    }

    /// <summary> The transcript to send as context, or null when empty. </summary>
    public string? Build()
    {
        lock (Mutex)
        {
            ExpireIfIdle();
            return Lines.Count == 0 ? null : string.Join("\n", Lines);
        }
    }

    public void Reset()
    {
        lock (Mutex)
            ResetLocked(keepTail: false);
    }

    private void ExpireIfIdle()
    {
        if (Lines.Count == 0)
            return;

        var idleMinutes = Math.Max(1, Plugin.Config.AiContextIdleMinutes);
        if (Environment.TickCount64 - LastActivity > idleMinutes * 60_000L)
            ResetLocked(keepTail: false);
    }

    private void ResetLocked(bool keepTail)
    {
        var seed = keepTail && Lines.Count > SeedLinesOnReset
            ? Lines.TakeLast(SeedLinesOnReset).ToList()
            : [];

        Lines.Clear();
        Lines.AddRange(seed);
        EstimatedTokens = seed.Sum(EstimateTokens);

        // A reset is a new conversation as far as the cache is concerned.
        ConversationId = NewConversationId();
    }
}

/// <summary>
/// Holds one <see cref="SceneBuffer"/> per chat tab, so a roleplay tab keeps
/// its own scene separate from general chatter.
/// </summary>
public class SceneBufferManager
{
    private readonly Lock Mutex = new();
    private readonly Dictionary<Guid, SceneBuffer> Buffers = [];

    public SceneBuffer Get(Guid tabId)
    {
        lock (Mutex)
        {
            if (!Buffers.TryGetValue(tabId, out var buffer))
            {
                buffer = new SceneBuffer();
                Buffers[tabId] = buffer;
            }

            return buffer;
        }
    }

    public void Append(Guid tabId, string sender, string content)
    {
        Get(tabId).Append(sender, content);
    }

    public void ResetAll()
    {
        lock (Mutex)
            foreach (var buffer in Buffers.Values)
                buffer.Reset();
    }
}
