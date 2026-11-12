namespace IvaliceCore;

/// <summary>
/// Hooks game functions. In the game this is Reloaded.Hooks (see <see cref="Startup"/>);
/// in the offline check it only reports what would be hooked.
/// </summary>
internal interface IHooks
{
    /// <summary>
    /// Send calls of the game function at <paramref name="function"/> to
    /// <paramref name="detour"/> (an [UnmanagedCallersOnly] function pointer).
    /// Returns an address that calls the original function.
    /// </summary>
    nint Hook(nint function, nint detour, string name);
}

/// <summary>For the offline check: patches nothing, and "calls the original" by returning it.</summary>
internal sealed class ReportedHooks(Log log) : IHooks
{
    public nint Hook(nint function, nint detour, string name)
    {
        log.Info($"hooks: would hook {name} at 0x{function:X}");
        return function;
    }
}

/// <summary>What patches work with: the exe, the log, hooks, and the battle globals and building blocks, found once.</summary>
internal sealed class Game(GameImage image, Log log, IHooks hooks)
{
    private Battle? _battle;
    private Routines? _routines;
    private TurnCode? _turnCode;
    private RuleCode? _ruleCode;
    private BattleMap? _map;
    private UnitTables? _units;

    public GameImage Image { get; } = image;
    public Log Log { get; } = log;
    public IHooks Hooks { get; } = hooks;
    public Battle Battle => _battle ??= Battle.Find(Image);
    public Routines Routines => _routines ??= Routines.Find(Image, Battle);
    public TurnCode TurnCode => _turnCode ??= TurnCode.Find(Image, Battle, Routines);
    public RuleCode RuleCode => _ruleCode ??= RuleCode.Find(Image, Battle, Routines);
    public BattleMap Map => _map ??= BattleMap.Find(Image);
    public UnitTables Units => _units ??= UnitTables.Find(Image);
}
