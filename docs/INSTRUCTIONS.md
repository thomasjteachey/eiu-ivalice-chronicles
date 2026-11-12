# The instructions

The Event Instruction Upgrade's instructions as this port runs them, by opcode. The names are the FFHacktics wiki's
([Event Instructions](https://ffhacktics.com/wiki/Event_Instructions)); its pages say what each does on the PS1. The
behaviour here was worked out from the hack's own code, as *The Lion War ReMixed* carries it (version 1.4.3 or later:
it has `ExitTo` and `GenerateUnit`), and checked against ReMixed's events running in the remaster.

**Here:**

- **runs**: the upgrade runs it (`src/EventInstructionUpgrade/EventScript.cs`).
- **not yet**: the upgrade knows its length and steps past it, logging a note the first time ("isn't ported yet;
  passed over").
- **remaster's**: the remaster's own interpreter runs it, as in any event.

**Bytes** is the parameter bytes after the opcode, as the upgrade reads them. A variable parameter (var) is a
variable's id; `var 0` holds the last condition, as on the PS1.

## Control flow

| Op | Name | Bytes | Here | What it does |
|---|---|---|---|---|
| 0x00 | Not | 0 | runs | var 0 = 1 if var 0 is 0, else 0 |
| 0x01 | If | 0 | runs | var 0 ≠ 0 goes on; otherwise on past the matching ElseIf that holds, the Else, or the EndIf (nested If and Switch blocks are passed over) |
| 0x02 | ElseIf | 1 (var) | runs | while an If is skipping: taken if the var ≠ 0. Reached from a branch that ran: on past the EndIf |
| 0x03 | Else | 0 | runs | reached from a branch that ran: on past the EndIf |
| 0x04 | EndIf | 0 | runs | nothing |
| 0x05 | Switch | 1 (var) | runs | on past the Case with the var's value, else the Default, else the EndSwitch |
| 0x06 | Case | 2 (u16) | runs | nothing (a Switch's target) |
| 0x07 | Default | 0 | runs | nothing (a Switch's target) |
| 0x08 | Break | 0 | runs | on past the EndSwitch |
| 0x09 | EndSwitch | 0 | runs | nothing |
| 0x0A | DefineSub | 1 (id) | runs | reached in the flow: its body is passed over, on past its EndSub |
| 0x0B | EndSub | 0 | runs | back to after the CallSub (a stack 128 deep) |
| 0x0C | CallSub | 1 (id) | runs | runs the DefineSub with that id, looked for from the event's start |
| 0x0D | GoTo | 1 (label) | runs | on from the GoToTarget with that label, looked for from the event's start |
| 0x0E | GoToTarget | 1 (label) | runs | nothing (a GoTo's target) |
| 0x0F | ExitTo | 3 (label, type, levels) | runs for type 3 | type 3: leaves that many CallSubs, then GoTo the label. Other types go on |
| 0x2F | BlockLoop | 4 (count, var, s16 offset) | runs | while the var ≠ 0: back by the offset (or on, if positive), or with offset 0 to the start of the parallel block (after its 0x2A) it's in. With a count of 2 or more the var counts down from count − 1 each time round |
| 0xD1, 0xD2 | JumpForward, ForwardTarget | 1, 1 | runs | vanilla's, run by the upgrade in its events: 0xD1 n goes on after the next 0xD2 n, or to the END when there's none (the remaster's own would restart the event) |

## Variables

| Op | Name | Bytes | Here | What it does |
|---|---|---|---|---|
| 0xAC | LUI | 4 (var, u16) | runs | var = value << 16 |
| 0xAD | (LUI from a variable) | 4 (var, var) | runs | var = the other var << 16. In the build ReMixed carries; not on the wiki's list |
| 0xAE | SET | 4 (var, u16) | runs | var = value |
| 0xAF | SETVar | 4 (var, var) | runs | var = the other var |
| 0xB0–0xBE | ADD … ZERO | 4 | remaster's | the remaster's arithmetic; the upgrade's words (0xB00–0xB7F) work in it too |
| 0xA0–0xA5 | LTE … GT | 0 | remaster's | the remaster's comparisons (var 0 against var 1, into var 0) |
| 0xBF | Random | 3 (var, u16 modulus) | runs | var = a random 0–0x7FFF modulo the modulus (none for 0) |

Variables 0x00–0x7F are 32-bit words in the remaster's own variable array, read and written directly: the game's own
getter and setter use var 0 as scratch (so an If would never see it change) and cut a negative value (any PS1 address)
to its low byte. Flags and four-bit fields above 0x80 go through the game's getter and setter. **0xB00–0xB7F** are the upgrade's own words, kept
by the mod (the remaster's variables stop at 0x3FF); see [WRITING-EVENTS.md](WRITING-EVENTS.md#variables).

## Memory

| Op | Name | Bytes | Here | What it does |
|---|---|---|---|---|
| 0xC1 | LoadAddress | 5 (var, u16 offset, base var, length) | runs | var = the 1–4 bytes at [base var] + offset, a PS1 address; nothing when the base is −1 |
| 0xC4 | SaveAddress | 5 (var, u16 offset, base var, length) | runs | the 1–4 bytes at [base var] + offset = var |
| 0xC2 | UnitAddress | 2 (id, var) | runs | var = the PS1 address of the battle unit with that battle id (+0x161), else that sprite set (+0x00), else 0 |
| 0xC3 | WorldUnitAddress | 2 (id, var) | runs | var = the PS1 address of the roster slot with that sprite set or name id, else 0 |
| 0xE6 | AlterParameter | 2 (spec, var) | runs | writes (spec >> 6) + 1 bytes of the var into the next instruction (past other AlterParameters), at its parameter offset spec & 0x3F |
| 0xE7 | GetTileUnit | 3 (x, y, level) | runs | var 0 = the PS1 address of the unit on that tile, else 0 |
| 0xE8 | LoadTileData | 5 (var, byte, x, y, level) | runs | var = that byte (0–7) of the map tile |
| 0xE9 | SaveTileData | 5 (var, byte, x, y, level) | runs | that byte of the map tile = var |

PS1 addresses go through a map of the remaster's memory: [MEMORY-MAP.md](MEMORY-MAP.md).

## Units, the party, the battle

| Op | Name | Bytes | Here | What it does |
|---|---|---|---|---|
| 0xE0 | GenerateUnit | 12 | runs | makes a battle unit in slot p3 from a battle-data record built from its parameters, from a roster slot or from another unit, the way the remaster's battle setup makes a battle's units; the event then shows it with vanilla's add-unit instructions. A mod sees the record's preset byte (+0x25) through `OnUnitPreset` |
| 0xC7 | RosterHasUnit | 3 (flags, u16 id) | runs | var 0 = whether the roster has the character (flag 0x80; by name id with flag bit 0, else by sprite set). The PS1's second list (flag 0x40) isn't mapped |
| 0x8D | Inventory | 3 (op, item, value) | runs | the high nibble of op picks what's counted (2: owned and equipped; 3: the Poachers' Den, **not run**; others: owned), the low nibble what's done: 0–4 compare with the value into var 0 (=, ≤, ≥, <, >), 6/7 add or take away the value, 8 the count into var value, 9/A/B set, add or take away var value. Counts stop at 99 with the equipped ones |
| 0xC5 | UpdateUnit | 2 | not yet | |
| 0xC6 | TransformUnit | 3 (var, flags, job) | not yet | on the PS1: changes a unit's or roster slot's job, its equipment back to the inventory, its raw stats rescaled for the new job |
| 0xCC | ResetTurnCounter | 1 (id) | not yet | |
| 0x15 | ResetUnitCalculations | 0 | not yet | |

## Scenes and the game

| Op | Name | Bytes | Here | What it does |
|---|---|---|---|---|
| 0x14 | PlayTrack | 1 (track) | runs | plays the track the way vanilla's CallFunction 0x0C plays its own |
| 0x17 | GraphicTimeout | 1 | not yet | |
| 0x9A | ResetTextIncrement | 0 | runs | the text values' index = 0 |
| 0x9B | PushTextVal | 2 (u16) | runs | the text value at the index = the value; the index steps on (wrapping at 0x80) |
| 0x9C | PushTextVar | 1 (var) | runs | the same with a variable's value |
| 0x9D | SetTextVal | 3 (index, u16) | runs | the text value at that index = the value |
| 0x9E | SetTextVar | 2 (index, var) | runs | the same with a variable's value |
| 0x9F | ButtonPressGoTo | 3 (mask, mask, label) | not yet | (the buttons themselves are mapped: LoadAddress on PS1 0x80045944 reads them) |
| 0xCD | PostEvent | 3 | runs | the current event's (var 0x27) word in the per-event map settings = (p2 << 10) + (p0 << 8 \| p1) |
| 0xCE | LoadConditionals | 2 (u16 n) | runs | event n's battle event conditions from the table a mod gives (`SetConditionals`) into the remaster's |
| 0xCF | Routine | 4 (u32 address) | runs | the native routine a mod registered at that PS1 address (`AddRoutine`); any other is passed over with a note |
| 0xEA | LoadENTD | 4 | not yet | read the disc on the PS1 |
| 0xEB | LoadENTDUnit | 4 | not yet | |
| 0xEC | ConditionalDefeat | 1 | not yet | |
| 0xED | LoadDatabase | 2 | not yet | read the disc on the PS1 |
| 0xEE | GetTableSize | 2 | not yet | |
| 0xEF | FetchTableRow | 2 | not yet | |

The text values are kept in the upgrade's words (PS1 0x80165EF4, `IEventInstructions.Words`). The remaster's message
boxes don't show them by themselves: see [WRITING-EVENTS.md](WRITING-EVENTS.md#text-values).

## The hack's changes to vanilla instructions

The PS1 hack also changes some of vanilla's own instructions. In the remaster those are the remaster's, and none of
these changes is ported:

- `Effect` (0x18) naming a caster;
- the improved `Focus` (0x1F), allowing for a jumping unit's height;
- `ChangeStats` (0x83), `RemoveUnit` (0x3D) and `Draw` (0x44) upgraded;
- `InflictStatus` (0x92) adding any status;
- choice boxes answering button presses, the cursor wrapping round, and `{stayopen}` in `DisplayMessage` (0x10);
- `DismissUnit` (0x7A) on the party leader;
- mounted units' special event handling removed;
- units' turn counters going on while they're dead.

An event that leans on one of these behaves as vanilla's instruction does in the remaster.

## Battle event conditions

`LoadConditionals` loads records that the game's condition check reads each turn. The PS1 hack has condition types
of its own as well (a unit's status, a team on a tile, a unit's finished turns: types 0x14, 0x15, 0x25 changed, 0x26,
0x27). Those aren't part of this mod: Ivalice ReMixed checks them in its own scenarios. A mod whose conditions use them
needs its own check for now.
