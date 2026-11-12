namespace IvaliceCore;

/// <summary>
/// Logs each distinct line once, up to a limit: enough to check a mechanic
/// against what the game shows, without flooding the log with the AI's damage
/// previews (which repeat the same results).
/// </summary>
internal static class Tracer
{
    private static readonly HashSet<string> Seen = [];

    /// <summary>Where every line goes as well, each time (ReMixed's test recorder); none by default.</summary>
    internal static Action<string>? Sink;

    public static void Line(Log log, string line)
    {
        Sink?.Invoke(line);
        lock (Seen)
        {
            if (Seen.Count >= 128 || !Seen.Add(line))
                return;
        }
        log.Info("trace " + line);
    }
}
