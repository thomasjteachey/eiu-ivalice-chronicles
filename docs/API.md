# Building a mod on the upgrade

The upgrade runs the instructions. A mod whose events use it gives it what only that mod knows: native routines,
battle conditions, memory and variables of its own. It does that through `IEventInstructions`, which the upgrade
exports to the mods that depend on it (`src/EventInstructionUpgrade.Interfaces/IEventInstructions.cs`).

## Getting it

1. List the upgrade in your mod's `ModConfig.json`, so Reloaded-II loads it (and its hooks) before your mod:
   ```json
   "ModDependencies": ["reloaded.sharedlib.hooks", "fftivc.utility.eventinstructionupgrade"]
   ```
2. Reference `EventInstructionUpgrade.Interfaces` (this repo as a git submodule and a `ProjectReference`, or the DLL
   from a build). Ship the DLL beside yours. Reloaded-II hands every mod the upgrade's copy, as it exports it.
3. Ask Reloaded-II for it at startup:

```csharp
using EventInstructionUpgrade.Interfaces;
using Reloaded.Mod.Interfaces;

public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
{
    var loader = (IModLoader)loaderApi;
    IEventInstructions? events = null;
    loader.GetController<IEventInstructions>()?.TryGetTarget(out events);
    if (events is not { Installed: true })
    {
        // Not loaded, or its hooks didn't go in (its own log lines say why).
        return;
    }

    // Routine (0xCF) 0x80151000 in your events runs this.
    events.AddRoutine(0x80151000, () =>
    {
        var level = events.GetVariable(0x70);
        // ...
    });

    // LoadConditionals (0xCE) n loads event n's conditions from this table.
    var dir = loader.GetDirectoryForModId(modConfig.ModId);
    events.SetConditionals(File.ReadAllBytes(Path.Combine(dir, "conditions.bin")));

    // See the upgrade's notes and errors in your own log or test recorder.
    events.OnMessage(note => { /* ... */ }, error => { /* ... */ });
}
```

## IEventInstructions

| Member | What it's for |
|---|---|
| `bool Installed` | whether the hooks are in the game. Everything else does nothing useful without them |
| `byte[] Marker` | the bytes an event's code starts with for the upgrade to run it (`0E 2C 0E 3C`) |
| `int MaxCode` | how long an event's code can be (0x2000 bytes) |
| `void MapMemory(uint address, int length, nint place)` | PS1 `address` and the `length` bytes after it live at `place`, for `LoadAddress` and `SaveAddress`. A later map of the same bytes wins over the upgrade's |
| `void AddVariables(holds, get, set)` | event variables your mod keeps itself, for ids 0x80 and up: for an id `holds` says yes to, the upgrade's instructions (SET, LoadAddress' base, Switch, …) and `GetVariable`/`SetVariable` go to `get` and `set`. The remaster's own instructions (its arithmetic and comparisons) still use the game's storage for that id |
| `int GetVariable(int id)`, `void SetVariable(int id, int value)` | an event variable as the events see it, your own and the upgrade's words (0xB00–0xB7F) included |
| `nint Words` | the upgrade's 0x100 words at PS1 0x80165EF4: variables 0xB00–0xB7F at word 0 on, and the text values from `PushTextVal` and the rest |
| `void AddRoutine(uint address, Action routine)` | `Routine` (0xCF) at that PS1 address runs `routine`. Read its inputs with `GetVariable` (the PS1 routines take theirs from variables) |
| `void SetConditionals(byte[] conditions)` | `LoadConditionals`' (0xCE) table: 0x220 bytes per event number, 0x20 of set offsets (16 halfwords) then 0x200 of records, as the PS1 keeps them |
| `void AddStepObserver(Action<nint> observer)` | called with each instruction's address just before it runs, in upgrade events. For changing an instruction as it's reached (a line id a mod moves), or following along |
| `void OnGraphicWritten(Action<uint> listener)` | an event wrote a battle unit's graphic (PS1 +0x15F): the unit's index. The remaster works a unit's look out again from its sprite set when a box shows its face; a mod that wants the event's look kept marks the unit here |
| `void OnUnitPreset(Action<int> listener)` | `GenerateUnit` (0xE0): the record's +0x25 before the unit is made, and −1 after. The remaster's battle data uses that byte for something else, so the upgrade clears it for the unit making and tells you instead |
| `void OnMessage(Action<string> note, Action<string> error)` | every note and error the upgrade logs |
| `int Next(nint code, int offset)` | the offset of the instruction after the one at `offset`, by the upgrade's lengths |
| `int PhaseBlock(nint code, int offset, int size)` | where `0xD1 n` at `offset` goes on: after the next `0xD2 n`, or the END |
| `nint RunningCode`, `int LastOffset`, `int LastOpcode`, `bool TraceSteps` | for a test harness: the running upgrade event's code, the last instruction fetched, and a log line per instruction |

## Things to know

- **When things run.** The upgrade's hooks are in before your mod's `StartEx` runs (the dependency sees to that), and
  your registrations take effect at once. Everything you give it is called on the game's thread, inside the event
  runner, while the event waits: keep it quick.
- **Exceptions.** An exception from your routine or observer is caught, logged as an error, and the event goes on past
  that instruction.
- **Order.** Observers run in the order they were added, before the instruction runs. Memory maps and variables
  added later win over earlier ones.
- **PS1 addresses.** A routine that wants a unit gets the PS1 address an event computed (`UnitAddress`). Turn it back
  into the remaster's unit with the map in [MEMORY-MAP.md](MEMORY-MAP.md): battle units start at 0x801908CC,
  0x1C0 bytes each, where the remaster's are 0x200.
