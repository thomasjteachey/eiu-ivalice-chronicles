namespace IvaliceCore;

/// <summary>
/// Where the mod's messages go: Reloaded-II's log in the game (see
/// each mod's startup), the console in the offline check.
/// </summary>
internal sealed class Log(Action<string> info, Action<string> warn, Action<string> error)
{
    public void Info(string text) => info(text);
    public void Warn(string text) => warn(text);
    public void Error(string text) => error(text);
}
