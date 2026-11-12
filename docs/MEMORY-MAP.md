# PS1 addresses in the remaster

Upgrade events deal in the PS1's addresses: `UnitAddress` gives a battle unit's, `WorldUnitAddress` a roster slot's,
`LoadAddress` and `SaveAddress` read and write at an address plus an offset. The remaster keeps the same things
elsewhere and often laid out differently, so the upgrade maps each PS1 byte to the remaster's
(`src/IvaliceCore/Ps1Memory.cs`).

A byte with no place in the remaster goes to a store of the mod's own: it reads back what an event wrote (0 before
that), and it's noted once in the log. Mods add places of their own with `IEventInstructions.MapMemory`.

## Battle units

PS1 0x801908CC, 21 units of 0x1C0 bytes; the remaster's are 0x200 bytes each, in the same order.

| PS1 offset | Remaster offset | What |
|---|---|---|
| +0x00–0x19 | the same | sprite set, unit slot, ids, job, team (+0x05), gender (+0x06), skillsets |
| +0x1A–0x20 | +0x1A + 2 × (n − 0x1A) | equipment, a byte each; the remaster keeps halfwords, and this is their low byte |
| +0x21–0x26 | +7 | EXP, level, Brave, Faith |
| +0x28–0x4D | +8 | HP, MP, stats, CT, Move, Jump, X (+0x47), Y, level, equip types (the remaster has a fifth at +0x56) |
| +0x4E–0x71 | +9 | innate statuses, immunities, statuses, their counters, elements |
| +0x72–0x80 | +9 | raw HP, MP, Speed, PA, MA, 3 bytes each |
| +0x8B–0x95 | +9 | reactions, supports, movement abilities |
| +0x96–0xD1 | +9 | unlocked jobs, then learned abilities: 3 bytes for each of the PS1's 19 jobs with abilities (the remaster has 22; the first 19 are the same jobs) |
| +0xD2–0xDB | +0x12 | job levels, a nibble a job, the even index in the high nibble |
| +0xDC–0x103 | +0xF0 on | JP by job, 20 halfwords (the remaster has 23; Squire to Mime are the first 20 in both). A JP written stops at 9999 |
| +0x104–0x12B | +0x11E on | total JP by job, 20 halfwords |
| +0x12C–0x13B | (+0x14C, UTF-8) | the name, in the PS1's character set; once it's written whole (its 0xFE or 0xFF end) it's written over the remaster's UTF-8 name |
| +0x15C–0x162 | +0x30 | +0x15E–0x160 the job's graphic, portrait and palette, +0x161 the battle data's unit id, +0x162 the job's skillset |
| +0x163 | +0x194 | (a halfword in the remaster) |
| +0x164 | +0x196 | |
| +0x16C–0x16D | +0x1FC | the name id |
| +0x182–0x18C | +0x32 | |
| +0x1B8–0x1BF | +0x34 | +0x1B8 and +0x1B9 cleared with the turn's flags; +0x1BA the base team (read only: see below); the second status copy |

Those from +0x72 to +0x12B are what a unit build copies from its roster slot (the PS1's SCUS 0x8005B1EC, the
remaster's 0x276758), so they line up as the roster's do. Anything else in a unit isn't mapped yet.

**Read only:** a unit's base team (PS1 +0x1BA, the remaster's +0x1EE, the team it plays by). An event that clears it
(as ReMixed's random battles' setup does to every unit it counts) would leave the remaster's battle with no enemies,
so writes there go to the mod's store instead.

## The roster

PS1 0x80057F74, 20 slots of 0x100 bytes; the remaster's slots are 0x258 bytes and it has more of them.

| PS1 offset | Remaster offset | What |
|---|---|---|
| +0x00–0x0D | the same | sprite set, slot, ids, job, … |
| +0x0E–0x14 | +0x0E + 2 × (n − 0x0E) | equipment (the low bytes of the remaster's halfwords) |
| +0x15–0x63 | +7 | EXP, level (+0x16), Brave, Faith, raw stats, unlocked jobs, learned abilities (19 sets of 3 bytes from +0x2B, the PS1's jobs with abilities) |
| +0x64–0x6D | +0x10 | job levels, a nibble a job, the even index in the high nibble (the remaster keeps 12 bytes from +0x74, after its 22 ability sets) |
| +0x6E–0x95 | +0x80 on | JP by job, 20 halfwords (the remaster has 23; Squire to Mime are the first 20 in both). A JP written stops at 9999, as the remaster's own JP add does |
| +0x96–0xBD | +0xAE on | total JP by job, 20 halfwords |
| +0xBE–0xCD | (+0xDC, UTF-8) | the name, as a battle unit's |
| +0xCE–0xCF | +0x230 | the name id (to be confirmed) |
| +0xD0 | +0x11E | the errand byte |

The remaster has more jobs (22 with abilities, 23 with JP); Squire to Mime are the first 20 in both, so each PS1 table
maps onto the start of the remaster's.

## Globals

| PS1 address | Bytes | What | Remaster |
|---|---|---|---|
| 0x8005771C | 0x2AC | the event variables: 0x80 words, the flags a bit each from +0x200, the four-bit fields from +0x25C | the variable array, the same layout |
| 0x80045944 | 4 | the buttons held (controller 1 in the low half: Select 0x100, Start 0x800, Cross 0x40, …) | made from the pad and the remaster's input actions whenever an event reads them |
| 0x80045948 | 4 | the buttons held the frame before | the same |
| 0x8004594C | 4 | the buttons newly pressed | the same |
| 0x800475E0 | 0x20 | the battle event condition sets' offsets (16 halfwords) | its condition check's (0xD40040) |
| 0x80049A18 | 0x200 | the battle event condition records | its condition check's (0xD3F550) |
| 0x8004E5D0 | 0x6EE | the per-event map settings, a halfword per event number (`PostEvent` writes them) | the world setup's table (0xD3F950), saved with the game |
| 0x80165EF4 | 0x400 | the upgrade's words: variables 0xB00–0xB7F and the text values | the mod's own (`IEventInstructions.Words`) |
| 0x80193844 | 2 | the enemies' level sum | the battle setup's |
| 0x80193920 | 1 | the enemies' highest level | the battle setup's |

Remaster addresses are RVAs in `FFT_enhanced.exe` (November 2026); the mod finds each by a byte pattern, so they move
with game updates without breaking it.

Not mapped by the upgrade: the PS1's skillset types (SCUS 0x80065CB4), read only, which Ivalice ReMixed maps for its own
events, and the PS1's second character list (0x800579C8, `RosterHasUnit` flag 0x40).
