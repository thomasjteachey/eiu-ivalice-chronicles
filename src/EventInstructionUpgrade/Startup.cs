using System.Drawing;
using EventInstructionUpgrade.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace EventInstructionUpgrade;

/// <summary>Reloaded-II entry point: installs the upgrade's hooks at startup and offers <see cref="IEventInstructions"/>
/// to the mods that depend on it.</summary>
public class Startup : IMod
{
    private const string Prefix = "[Event Instruction Upgrade] ";
    private readonly Api _api = new();

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        var loader = (IModLoader)loaderApi;
        var logger = (ILogger)loader.GetLogger();
        var log = new Log(
            text => logger.WriteLine(Prefix + text),
            text => logger.WriteLine(Prefix + text, Color.Orange),
            text => logger.WriteLine(Prefix + text, Color.Red));
        try
        {
            var game = new Game(GameImage.Main(), log, Hooks.From(loader));
            EventHooks.Install(game);
        }
        catch (Exception e)
        {
            log.Error($"not installed: {e}");
        }
        // Offered even when the hooks didn't go in (Installed says so), so the mods built on it can say why.
        loader.AddOrReplaceController<IEventInstructions>(this, _api);
    }

    // The hooks change the game's code, so the mod can't be suspended or unloaded.
    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => { };
}

/// <summary>The types this mod shares with the mods that use it (Reloaded-II loads them once for all).</summary>
public class Exports : IExports
{
    public Type[] GetTypes() => [typeof(IEventInstructions)];
}
