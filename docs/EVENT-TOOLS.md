# Event tools

`tools/eiu.py` is for making and porting events without a hex editor. It needs Python 3.11 or later and nothing
else. Run it from the repo's root:

```bash
python -I tools/eiu.py --help
```

| Command | What it does |
|---|---|
| `disasm EVENT.e` | an event as a readable script (`EVENT.evt`) |
| `build SCRIPT.evt` | a script as an event the game reads (`SCRIPT.e`), the upgrade's marker in front |
| `patch HACK.ppf VANILLA.bin HACK.bin` | a PS1 hack's disc, from its PPF patch and the disc it was made for |
| `scan --disc HACK.bin` | what a hack's events use: how many use the upgrade, which instructions it doesn't run yet, and the hack's own routines its events call |
| `convert --disc HACK.bin --event N` | a hack's PS1 event as the remaster's: `.e`, `.evt` and its text as numbered lines |
| `extract DISC.bin PATH OUT` | one file off a PS1 disc (`EVENT/TEST.EVT`, `BATTLE.BIN`, …) |

The same pieces are a Python package, `tools/eiu_events`, for a mod's own build script (see [In a build
script](#in-a-build-script)).

## Writing events as scripts

```bash
python -I tools/eiu.py disasm event110.e        # event110.evt
python -I tools/eiu.py build event110.evt       # event110.e, ready for script/enhanced/
```

A script is one instruction a line, by the name on the FFHacktics wiki's
[instruction list](https://ffhacktics.com/wiki/Event_Instructions), with its parameters in brackets:

```
# Zalbag thanks the party if he lived.
UnitAddress(0x85, 0x70)                 # var 0x70 = his address
LoadAddress(0x00, 0x0024, 0x70, 1)      # var 0 = his Brave
SET(0x0001, 70)
GTE()                                   # var 0 = Brave >= 70
If()
    DisplayMessage(0x10, 0x11, 315160, 0x85, 0x00, 0x00, 0, 0, 0, 0x02, 0x00)
Else()
    Wait(60)
EndIf()
EventEnd()
```

- **Numbers** can be hex (`0x1F`) or decimal (`31`, `-4`). `disasm` writes each parameter the way it's easiest to
  read: ids and flags in hex, coordinates and timers in decimal, line ids in decimal (as the game's text files number
  them).
- **Names** don't care about case. An opcode with no name is written `Op` and its hex (`Op23`), and `Bytes(0x12, 0x34)`
  puts raw bytes in.
- **Indentation, blank lines and comments** (`#` to the end of the line) are only for people. `disasm` indents the
  insides of `If`, `Switch`, `DefineSub` and `BlockStart`, and leaves a blank line before each `GoToTarget` and
  `DefineSub`.
- **The marker** (`0E 2C 0E 3C`, which tells the upgrade to run an event) is left out of the script and put back by
  `build`. Use `build --no-marker` for one of the remaster's own events, which the upgrade should leave alone.
- **Round trips are exact.** `disasm` then `build` gives back the same bytes, so you can disassemble any event, change a
  line and build it.
- **Errors say where.** `line 12: SET takes 2 parameter(s), not 1`.
- **`--offsets`** ends each line with the instruction's offset as the upgrade's log gives it (`step 0x123`), for
  following a running event.

How many parameters an instruction takes, and their sizes, come from the interpreter's own table, so a script can't
put an instruction together wrong. What each parameter means is on the wiki's page for that instruction, and in
[INSTRUCTIONS.md](INSTRUCTIONS.md) for the upgrade's own.

`DisplayMessage` and `ChangeDialog` are longer in the remaster than on the PS1, because they name a line of text by a
32-bit id ([WRITING-EVENTS.md](WRITING-EVENTS.md#messages)). Scripts are in the remaster's layout unless you pass
`--ps1`.

## Bringing a PS1 hack's events over

1. **Its disc.** Hacks are shared as PPF patches for the US disc (SCUS-94221), as a raw `.bin` with 2352-byte sectors:
   ```bash
   python -I tools/eiu.py patch hack.ppf "Final Fantasy Tactics (USA).bin" hack.bin
   ```
   The patch checks it's the disc it was made for. The original isn't changed.
2. **What it needs:**
   ```bash
   python -I tools/eiu.py scan --disc hack.bin
   ```
   ```
   291 event(s) use the upgrade's instructions (67278 uses)
   Instructions the upgrade doesn't run yet: TransformUnit (0xC6) x4, GraphicTimeout (0x17) x2, ...
   Routine (0xCF) calls into the hack's own code, each needing a C# port (docs/API.md, AddRoutine): 0x80151000 x99, ...
   ```
   `-v` gives a line per event. Instructions the upgrade doesn't run yet are skipped in the game (with a note in the
   log), and each native routine needs porting to C# by the mod that ships the events.
3. **An event:**
   ```bash
   python -I tools/eiu.py convert --disc hack.bin --event 0x6E --first-line 315160 --as 110
   ```
   This writes, in `converted/`:
   - `event110.e`: the event in the remaster's layout, ready to ship as `FFTIVC/data/enhanced/script/enhanced/event110.e`;
   - `event110.evt`: the same as a script, to read and change (then `build` it);
   - `event110.lines.txt`: its messages, numbered, with the line id each is shown as, to put in the game's text.

   `--event` is the hack's number for it, `--as` the number the remaster should load it as (by default the same).
   `--disc` can be `--test-evt TEST.EVT --battle-bin BATTLE.BIN` instead, files taken off the disc with `extract`. The
   hack's `BATTLE.BIN` gives its own instruction lengths, so a hack with another version of the upgrade converts right.

What `convert` does to an event:

- takes its code out of the disc's `EVENT/TEST.EVT` (each event's block: where its text starts, its code, its text);
- puts `DisplayMessage` and `ChangeDialog` in the remaster's layout, message *n* naming line `first-line + n − 1`, and
  moves an `AlterParameter` aimed past a message number 2 bytes on, to the same parameter;
- gives each box a style the remaster draws (it crashed on some of the PS1's), and menus the remaster's menu kind;
- puts a `GoTo` over a `DisplayMessage` for each line the code picks while it runs, never run but read by the remaster
  when the event loads, so it sizes those lines' boxes too;
- puts the marker in front and checks the size (0x2000 bytes at most).

What it doesn't do, and a mod may need: the text's way into the game (below); fitting long PS1 lines into the
remaster's smaller boxes; the hack's text codes that time its scenes (waits for a line to reach a point); and anything
the hack's events expect of its own code or data. `scan` lists the routines; the rest shows up as notes in the log
when the event runs ([WRITING-EVENTS.md](WRITING-EVENTS.md#seeing-what-it-did)).

### Lines of text

The PS1 keeps an event's text in its block; the remaster keeps all text in its own files, and an event's
`DisplayMessage` names a line by its id. `convert` reads the PS1 text into `.lines.txt`, one message a line:

```
1	315160	{E3:08}{hero}\n{E3:00}No guards. Passing strange.
2	315161	Zalbaag's mount.
```

What it can spell out it does; the rest stays as `{XX}` tokens (a control code and its byte as `{E3:08}`, here a
speaker's name in colour), and `\n` is a line break. The decoder knows vanilla's font: a hack that changed its font
(Ivalice ReMixed's has brackets at 0x8D and 0x8E) needs `CHARS` in `tools/eiu_events/text.py` adjusted.

**Getting lines into the game** isn't part of these tools yet. The remaster's event text is in Panzer files,
`nxd/text/scenario/scenarioNNNN.pzd`, each a list of lines (`Id`, `Line`, `Speaker`, …) that Nenkai's
[FF16Tools](https://github.com/Nenkai/FF16Tools) makes from YAML (`pzd-conv`). The game loads the files its
`ScionFiles` table lists, and a `ScenarioId` row gives a file to the events that use it; FF16Tools turns those tables
to SQLite and back. The remaster's own lines that events reach end at id 315159; Ivalice ReMixed's take ids from
315160 on (up to 0x4FFFF). Pick ids no other mod you'll run with uses.

## In a build script

```python
import sys
from pathlib import Path

sys.path.insert(0, "path/to/eiu-ivalice-chronicles/tools")   # e.g. a submodule
import eiu_events as eiu

with eiu.Disc("hack.bin") as disc:
    test_evt = disc.read(disc.find("EVENT/TEST.EVT"))
    lengths = eiu.read_lengths(disc.read(disc.find("BATTLE.BIN")))

code = eiu.event_code(test_evt, 0x6E, lengths)
messages = eiu.split_messages(eiu.event_text(test_evt, 0x6E))
remaster = eiu.to_remaster(code, lengths, first_line=315160, messages=len(messages), menus=eiu.menus(messages))
Path("event110.e").write_bytes(eiu.event_file(remaster))
```

To make your own choices on the way, call the steps yourself: `remaster_layout()` (it takes a `restyle` hook that
can change each box's style, place and centring), then your own changes, then `shadow_messages()`. Ivalice ReMixed's
build does that: its tavern's menus go in the remaster's speech windows through `restyle`, and it fits long lines and
times its scenes in between.

Everything is in `tools/eiu_events/`: `script.py` (scripts), `convert.py` (PS1 events), `text.py` (PS1 text),
`instructions.py` (names, parameters and lengths), `disc.py` and `ppf.py` (discs and patches).

## Tests

```bash
python -I -m unittest discover -s tests
```
