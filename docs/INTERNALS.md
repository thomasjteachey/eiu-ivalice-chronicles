# How it works

## The remaster's event runner

The remaster's event interpreter is a loop in `FFT_enhanced.exe` (the event runner, 0x23A868). Its per-instruction
fetch (0x23AFD8) reads the instruction at the running offset of the code buffer (`[0xD40A90]`, the script's size in a
u32 and the code from +4) and compares the opcode down a tree to its handler; no jump table. A finished handler adds
the instruction's length (from the length helper, 0x2CBE2C) and comes back round. Variables go through an executor
(0x242700) behind the getter, the setter, the arithmetic and the comparisons.

## The three hooks

`src/EventInstructionUpgrade/EventHooks.cs` installs them at startup:

1. **The fetch.** A jump at 0x23AFD8 goes to a stub that checks whether the running code starts with the marker. If
   it does, it calls `EventHooks.Step(code, offset)`. That runs the mod's step observers, then `EventScript.Step`,
   which runs the instruction if it's one of the upgrade's and returns the offset to go on from. The stub puts that
   offset where the runner's "next instruction" path reads it (`[rsp+0x44]`, then 0x23B63A) with the registers that
   path expects. For the remaster's own instructions `Step` returns −1, and the stub runs the moved bytes and goes
   back to the game's compare tree.
2. **The length helper.** A jump at 0x2CBE2C: inside marked code, the length comes from the upgrade's table
   (`EventScript.Lengths`); elsewhere the game's own runs. So the remaster's own scans (for labels, loops and blocks)
   walk upgrade code correctly.
3. **The variables' executor** (0x242700), hooked through Reloaded.Hooks: ids 0xB00–0xB7F, the upgrade's words, are
   done in the mod's store; everything else goes to the game's executor unchanged. This one works in every event,
   since the remaster stops its variables at 0x3FF and would otherwise end an event that touches 0xB00.

`Ps1Pad` makes the PS1's button words from the pad and the remaster's input actions whenever an event reads them,
since the remaster builds its own copy only during a battle's frames.

## Finding things

Nothing is at a fixed address: each place is found by a byte pattern (`GameImage.FindUnique`) and cross-checked
(a call's target, a global it reads), so a game update makes the mod log what it couldn't find instead of writing
somewhere wrong. The addresses in comments are RVAs as of November 2026, for reading along in a disassembler.

## The interpreter

`EventScript.Step` is a switch on the opcode. `Ours` lists the opcodes the upgrade handles before the remaster
(any of them in an upgrade event comes to `Step`), and `Done` the ones it runs. The rest of `Ours` are passed over
with a note. The control flow follows the PS1 hack's own code closely, quirks included: `If` and `Switch` scan forward
for their targets (nested blocks counted) and stop at END, `CallSub` and `GoTo` look for their targets from the
event's start, and the call stack is 128 deep.

PS1 addresses go through `Ps1Memory` (`src/IvaliceCore/`, [MEMORY-MAP.md](MEMORY-MAP.md)). `GenerateUnit` builds the
PS1's 40-byte battle-data record and has the remaster make the unit with its own battle setup's calls
(`CreateUnit`).

## Adding an instruction

1. Make sure its opcode is in `Ours` and its parameter bytes are right in `Lengths` (the PS1's table, with the two
   widened message instructions).
2. Add a `case` to `EventScript.Step` that does it and returns the next offset (`next`, or a jump's target).
3. Add the opcode to `Done`.
4. Give it a self-test in `src/EventInstructionUpgrade.Check/Program.cs`, built from made-up units and code as the
   others are, and run `build.py --exe`.
5. Mark it in [INSTRUCTIONS.md](INSTRUCTIONS.md).

If it needs something from the game, find it by a pattern in `EventHooks.NewScript` (or in `IvaliceCore` when
other mods could use it too) and hand it to the script, as `PlayMusic`, `Items` and `TileAt` are.

## Testing

`build.py --exe <FFT_enhanced.exe>` runs the offline check: it maps the exe from disk without starting the game,
installs the upgrade's hooks there with Reloaded.Hooks, and runs its self-tests. These cover the hook places, the
memory map, a program through the interpreter, `BlockLoop` and the tile instructions, the words through the hooked
executor, names and JP written as the PS1 has them, phase blocks, and `GenerateUnit`'s records. It doesn't run
events in the game; for that, ship one and read Reloaded-II's log.
