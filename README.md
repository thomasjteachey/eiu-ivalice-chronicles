# Event Instruction Upgrade for The Ivalice Chronicles

A port of **Xifanie's Event Instruction Upgrade** (EIU), the FFHacktics hack that gives Final Fantasy Tactics' event
scripts real control flow and memory access, to **Final Fantasy Tactics: The Ivalice Chronicles** (the 2025 PC
remaster, enhanced mode), as a [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II) mod.

With it, an event script can use `If`/`ElseIf`/`Else`, `Switch`/`Case`, subroutines and `GoTo`, 32-bit variables,
read and write the game's units and roster through their PS1 addresses (`LoadAddress`, `SaveAddress`,
`UnitAddress`), patch its own next instruction from a variable (`AlterParameter`), make units on the fly
(`GenerateUnit`), and call native routines that a mod supplies in C# (`Routine`).

It was written for [Ivalice ReMixed](#credits), the port of *The Lion War ReMixed*, whose event scripts are written
for the PS1 hack; any mod can use it the same way.

> **Unofficial.** This is a clean reimplementation in C#, not Xifanie's code, and it holds no game data and no part
> of any PS1 hack or ROM. The instruction set and its design are Xifanie's: see [Credits](#credits).

## What runs

- **42 of the upgrade's instructions** run in the remaster: the whole control flow, the variable sets, memory
  access, text values, `PlayTrack`, `BlockLoop`, `Inventory`, `RosterHasUnit`, `PostEvent`, `LoadConditionals`,
  `Routine`, `GenerateUnit`, `AlterParameter`, `GetTileUnit` and the tile reads and writes.
  [docs/INSTRUCTIONS.md](docs/INSTRUCTIONS.md) lists every one and what's not done.
- **Only events marked for it**: an event's code that starts with `0E 2C 0E 3C` runs through the upgrade; every other
  event is the remaster's own and is left alone.
- **The remaster's own instructions** (dialogue, camera, units, vanilla's arithmetic and comparisons) keep running
  in the game's interpreter, inside upgrade events too.

Not ported: the hack's changes to vanilla instructions (`Effect` with a caster, the improved `Focus`, and others; see
[docs/INSTRUCTIONS.md](docs/INSTRUCTIONS.md)), a handful of upgrade instructions no event tested so far uses, and
classic mode.

## For players

You don't install this on its own: a mod that needs it lists it as a dependency, and you install it with that mod.
In Reloaded-II, a dependency loads with the mod that needs it; you don't have to tick it. It needs Reloaded-II's
shared hooks mod (`reloaded.sharedlib.hooks`), which the FFT Mod Loader also uses, and works on the Steam Deck through
Reloaded-II as well.

## For modders

- **Event tools:** [docs/EVENT-TOOLS.md](docs/EVENT-TOOLS.md): `tools/eiu.py` turns events into readable scripts
  (the wiki's instruction names, one a line) and back, and brings a PS1 hack's events over: its disc from its PPF
  patch, a report of what its events need, and each event converted, with its text as numbered lines. Python only.
- **Writing events for it:** [docs/WRITING-EVENTS.md](docs/WRITING-EVENTS.md): the marker, where the files go, the
  remaster's message layout, the variables, an example.
- **Building a mod on it:** [docs/API.md](docs/API.md): `IEventInstructions`, which the mod exports to the mods that
  depend on it, for your native routines (`Routine`), your battle conditions (`LoadConditionals`), memory and
  variables of your own, and a look at each instruction as it runs.
- **The instructions:** [docs/INSTRUCTIONS.md](docs/INSTRUCTIONS.md).
- **PS1 addresses in the remaster:** [docs/MEMORY-MAP.md](docs/MEMORY-MAP.md): which unit and roster fields and
  globals `LoadAddress` and `SaveAddress` reach.
- **How it works, and changing it:** [docs/INTERNALS.md](docs/INTERNALS.md): the hooks, the interpreter, adding an
  instruction, testing.

## Building

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0), Python 3.11+, and Reloaded-II with the
shared hooks mod installed (the code builds against Reloaded-II's interfaces).

```bash
python -I build.py --reloaded "C:/path/to/Reloaded-II" --exe "C:/path/to/FFT_enhanced.exe" --install
```

This builds `out/fftivc.utility.eventinstructionupgrade/`, runs the offline check (it maps `FFT_enhanced.exe` from
disk, installs the upgrade's hooks on that copy and runs its self-tests, without starting the game), and copies the
mod into Reloaded-II's `Mods` folder. `--zip` also writes `out/fftivc.utility.eventinstructionupgrade.zip`, which
Reloaded-II installs when dropped on its window. `--reloaded` defaults to the `RELOADEDII` environment variable.

The upgrade's word variables (0xB00–0xB7F) start as BATTLE.BIN's bytes at PS1 0x80165EF4 on the PS1. Those are game
data, so they aren't here: without them the words start at 0. A mod that needs the PS1's starting values passes them
with `--words` (0x400 bytes), or ships `eiu-battle-words.bin` beside the DLL.

## Layout

```
src/EventInstructionUpgrade/             the mod: its hooks (EventHooks), the interpreter (EventScript), the API
src/EventInstructionUpgrade.Interfaces/  IEventInstructions, the API other mods compile against
src/IvaliceCore/                         what the upgrade is built on: the exe searched by byte patterns, hooks, the
                                         battle units and roster, the PS1 memory map, unit making
src/EventInstructionUpgrade.Check/       the offline check
tools/eiu.py, tools/eiu_events/          the event tools: scripts, PS1 events converted, discs and patches
tests/                                   the event tools' tests (python -I -m unittest discover -s tests)
mod/ModConfig.json                       the mod's Reloaded-II config
build.py                                 builds, checks, installs and zips the mod
docs/                                    the documentation
```

## Branching off

Fork it, change what you need, and build it as above. A few things to know:

- **One copy at a time.** Two mods that both hook the event runner would fight over it, so a fork that changes the
  upgrade should keep the mod id `fftivc.utility.eventinstructionupgrade` and replace this one, not run beside it.
  Give it a new id only if it no longer hooks the same places.
- **The API is the contract.** Mods built on the upgrade use `IEventInstructions` only. Add to it rather than change
  what's there, and they keep working.
- **IvaliceCore is internal.** Its types are `internal`, shared with named assemblies (`InternalsVisibleTo` in
  `src/IvaliceCore/IvaliceCore.csproj`). A mod that compiles it in, as Ivalice ReMixed does, adds its assembly name
  there.
- **Game updates.** Everything is found by byte patterns, and the offline check says which one no longer matches.
  Addresses in the comments are RVAs in `FFT_enhanced.exe` as of November 2026.

## Credits

- **Xifanie**: the Event Instruction Upgrade, the PS1 hack this ports, and its instruction set
  ([FFHacktics wiki](https://ffhacktics.com/wiki/Event_Instruction_Upgrade),
  [its instructions](https://ffhacktics.com/wiki/Event_Instructions)).
- **The Lion War ReMixed** and its authors: its events are what this was ported for and tested with, in
  Ivalice ReMixed.
- **Nenkai**: the FFT Mod Loader, which ships a mod's event files to the remaster.
- **Sewer56**: Reloaded-II and Reloaded.Hooks.
- **The FFHacktics community**: the wiki and its tools.

## License

The code here is under the [MIT License](LICENSE). Final Fantasy Tactics is Square Enix's; the Event Instruction
Upgrade's design is Xifanie's.
