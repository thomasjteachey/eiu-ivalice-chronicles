# Writing events for the upgrade

## Where an event goes

The remaster reads event `N` from `script/enhanced/eventNNN.e` (`NNN` the event's number in decimal, three digits).
A mod ships its own through the [FFT Mod Loader](https://www.nexusmods.com/finalfantasytacticstheivalicechronicles/mods/4),
in its folder's `FFTIVC/data/enhanced/script/enhanced/`. The game's loader puts the script's size in a u32 and the
file's bytes after it, where its interpreter starts, so the file holds the event's code and nothing else.

## The marker

The upgrade runs an event only when its code starts with these four bytes:

```
0E 2C 0E 3C        GoToTarget 0x2C, GoToTarget 0x3C
```

They're two labels and do nothing when run. Don't use labels 0x2C and 0x3C yourself (a `GoTo 0x2C` would land on the
marker). Events without the marker are the remaster's own and the upgrade leaves them alone, so a mod can ship both
kinds side by side.

An event's code, marker included, can be up to 0x2000 bytes.

## Instruction lengths

Inside an upgrade event, every instruction's length is the upgrade's (`EventScript.Lengths`): the PS1's, with the
remaster's layout for the two message instructions below. That's what lets the remaster's own scans (labels, loops)
and the upgrade's (If, Switch, CallSub) walk the same code. The remaster's own 0xE6, a variable-length instruction,
isn't available in an upgrade event: there 0xE6 is `AlterParameter`.

## Messages

The remaster names a line of text with a 32-bit line id where the PS1 had a 16-bit message number, so two
instructions are longer than on the PS1. Write them as the remaster's own events do (its `.e` files are the
reference):

- **DisplayMessage (0x10)**, 17 parameter bytes: the PS1's 14 with the message number at +2 widened to a u32 line id
  (0xFFFFFFFF for none), and one byte more at the end (Ivalice ReMixed sets it to centre a box's text; 0 otherwise).
- **ChangeDialog (0x51)**, 7 parameter bytes: the PS1's 5 with the message number at +1 widened to a u32 line id.

Each parameter after the message number sits 2 bytes later than on the PS1, which matters to an `AlterParameter`
aimed at one. New lines go in the remaster's text tables, which the FFT Mod Loader can add to.

## Variables

| Ids | What they are |
|---|---|
| 0x00–0x7F | 32-bit words in the remaster's variable array. Var 0 holds the last condition (If, Not, the comparisons, GetTileUnit, RosterHasUnit, Inventory's comparisons). Var 0x27 is the running event's number |
| 0x80–0x35F | flags, a bit each, the remaster's |
| 0x360–0x3FF | four-bit fields, the remaster's |
| 0xB00–0xB7F | the upgrade's own 32-bit words, kept by the mod (the remaster's variables stop at 0x3FF). They work in SET, the arithmetic and the comparisons like any other. They aren't reset between events and aren't saved with the game |

A mod can keep variables of its own in any of the remaster's ranges (`IEventInstructions.AddVariables`).

`LoadAddress` and `SaveAddress` reach the variables as memory too: PS1 0x8005771C is the array, laid out as the
remaster's (0x80 words, then the flags, then the four-bit fields).

## PS1 memory

`LoadAddress`, `SaveAddress`, `UnitAddress`, `WorldUnitAddress` and `GetTileUnit` deal in the PS1's addresses, and
the upgrade maps them onto the remaster's memory: [MEMORY-MAP.md](MEMORY-MAP.md) has what's there. An address with no
place in the remaster is kept in a store of the mod's own, so an event reads back what it wrote, and it's noted once in
Reloaded-II's log ("has no place in the remaster yet").

## Text values

`ResetTextIncrement`, `PushTextVal`, `PushTextVar`, `SetTextVal` and `SetTextVar` keep numbers for a message to show,
in the upgrade's words at PS1 0x80165EF4. The remaster's message boxes don't show them by themselves. A mod that needs
them reads the words (`IEventInstructions.Words`) and puts them in its text: Ivalice ReMixed swaps in a line with the
numbers filled as the game asks for it.

## Native routines and battle conditions

`Routine` (0xCF) runs a native routine that a mod registers at that PS1 address (`AddRoutine`); with none
registered it's passed over. `LoadConditionals` (0xCE) loads battle event conditions from a table a mod gives
(`SetConditionals`). See [API.md](API.md).

## An example

```
0E 2C 0E 3C              the marker
C2 85 70                 UnitAddress: var 0x70 = the PS1 address of the battle unit with battle id 0x85
C1 00 24 00 70 01        LoadAddress: var 0 = 1 byte at [var 0x70] + 0x24 (its Brave)
AE 01 00 46 00           SET: var 1 = 70
A1                       GTE: var 0 = var 0 >= var 1
01                       If
AE 10 00 01 00             SET: var 0x10 = 1
03                       Else
AE 10 00 02 00             SET: var 0x10 = 2
04                       EndIf
DB                       EventEnd
```

## Seeing what it did

Reloaded-II's log (its console, or `%APPDATA%/Reloaded-Mod-Loader-II/Logs/`) has the upgrade's lines, marked
`[Event Instruction Upgrade]`: that it's installed, each upgrade event that starts running, instructions it passes
over, and PS1 addresses with no place yet. A mod can also watch each instruction as it runs
(`AddStepObserver`) or turn on a line per instruction (`TraceSteps`).
