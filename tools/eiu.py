"""Event tools for the Event Instruction Upgrade (docs/EVENT-TOOLS.md has the walk-through).

Read and write events as scripts:
    python -I tools/eiu.py disasm event110.e                  # writes event110.evt, a readable script
    python -I tools/eiu.py build event110.evt                 # writes event110.e, ready for the game

Bring a PS1 hack's events over:
    python -I tools/eiu.py patch hack.ppf vanilla.bin hack.bin       # the hack's disc, from its PPF patch
    python -I tools/eiu.py scan --disc hack.bin                      # what its events use, and what the upgrade lacks
    python -I tools/eiu.py convert --disc hack.bin --event 0x6E --first-line 315160
                                                                     # its event 0x6E as event110.e, .evt and .lines.txt

Python 3.11+, standard library only.
"""

from __future__ import annotations

import argparse
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import eiu_events as eiu  # noqa: E402
from eiu_events.instructions import name  # noqa: E402


def number(text: str) -> int:
    return int(text, 0)


def load_disc_files(args: argparse.Namespace) -> tuple[bytes, bytes]:
    """TEST.EVT and the PS1 length table, from --disc or from --test-evt (and --battle-bin)."""
    if args.disc:
        with eiu.Disc(args.disc) as disc:
            evt, battle = disc.find("EVENT/TEST.EVT"), disc.find("BATTLE.BIN")
            if evt is None:
                sys.exit(f"{args.disc} has no EVENT/TEST.EVT: is it a Final Fantasy Tactics disc?")
            test_evt = disc.read(evt)
            lengths = eiu.read_lengths(disc.read(battle)) if battle else eiu.PS1_LENGTHS
        return test_evt, lengths
    if not args.test_evt:
        sys.exit("Give --disc (a PS1 disc image) or --test-evt (its EVENT/TEST.EVT).")
    lengths = eiu.read_lengths(Path(args.battle_bin).read_bytes()) if args.battle_bin else eiu.PS1_LENGTHS
    return Path(args.test_evt).read_bytes(), lengths


def cmd_disasm(args: argparse.Namespace) -> None:
    source = Path(args.file)
    out = Path(args.output) if args.output else source.with_suffix(".evt")
    script = eiu.disassemble(source.read_bytes(), remaster=not args.ps1, offsets=args.offsets,
                             header=f"{source.name}, as a script for the Event Instruction Upgrade (eiu.py build "
                                    f"makes it a .e file again)")
    out.write_text(script, encoding="utf-8")
    print(f"Wrote {out}")


def cmd_build(args: argparse.Namespace) -> None:
    source = Path(args.file)
    out = Path(args.output) if args.output else source.with_suffix(".e")
    try:
        code = eiu.assemble(source.read_text(encoding="utf-8"), remaster=not args.ps1, marker=not args.no_marker)
    except eiu.ScriptError as e:
        sys.exit(f"{source}: {e}")
    if len(code) > eiu.MAX_CODE:
        sys.exit(f"{source}: the event is 0x{len(code):X} bytes; the upgrade takes up to 0x{eiu.MAX_CODE:X}")
    out.write_bytes(code)
    print(f"Wrote {out} ({len(code)} bytes)")


def cmd_convert(args: argparse.Namespace) -> None:
    test_evt, lengths = load_disc_files(args)
    out_dir = Path(args.output)
    out_dir.mkdir(parents=True, exist_ok=True)
    stem = f"event{(args.as_number if args.as_number is not None else args.event):03d}"
    try:
        code = eiu.event_code(test_evt, args.event, lengths)
    except ValueError as e:
        sys.exit(str(e))
    messages = eiu.split_messages(eiu.event_text(test_evt, args.event))
    if messages and args.first_line is None:
        print(f"Note: event 0x{args.event:X} has {len(messages)} message(s) but no --first-line, so its "
              "DisplayMessages name no line (docs/EVENT-TOOLS.md, \"Lines of text\").")
    remaster = eiu.to_remaster(code, lengths, args.first_line, len(messages), eiu.menus(messages))
    try:
        data = eiu.event_file(remaster)
    except ValueError as e:
        sys.exit(f"event 0x{args.event:X}: {e}")
    (out_dir / f"{stem}.e").write_bytes(data)
    header = (f"PS1 event 0x{args.event:X} ({args.event}) converted for the remaster"
              + (f"; message n is line {args.first_line} + n - 1" if args.first_line is not None else ""))
    (out_dir / f"{stem}.evt").write_text(eiu.disassemble(data, lengths=lengths, header=header), encoding="utf-8")
    written = [f"{stem}.e", f"{stem}.evt"]
    if messages:
        lines = [f"# PS1 event 0x{args.event:X}'s messages. Number, line id, text ({{XX}}: codes not spelled out)."]
        for n, message in enumerate(messages, 1):
            line_id = args.first_line + n - 1 if args.first_line is not None else "-"
            lines.append(f"{n}\t{line_id}\t{eiu.decode(message)}".replace("\n", "\\n"))
        (out_dir / f"{stem}.lines.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
        written.append(f"{stem}.lines.txt")
    print(f"Wrote {', '.join(written)} in {out_dir}")


def cmd_scan(args: argparse.Namespace) -> None:
    """Each event with code: its size, and what it needs of the upgrade."""
    test_evt, lengths = load_disc_files(args)
    picked = args.events or range(eiu.event_count(test_evt))
    totals, missing, routines, events = Counter(), Counter(), Counter(), 0
    for n in picked:
        try:
            code = eiu.event_code(test_evt, n, lengths)
        except ValueError:
            continue
        used, at = Counter(), 0
        while at < len(code):
            op = code[at]
            used[op] += 1
            if op == 0xCF and at + 5 <= len(code):
                routines[int.from_bytes(code[at + 1:at + 5], "little")] += 1
            at += 1 + lengths[op]
        upgrade = {op: c for op, c in used.items() if op in eiu.UPGRADE}
        if not upgrade and not args.all:
            continue
        events += 1
        totals.update(upgrade)
        lacking = {op: c for op, c in upgrade.items() if op not in eiu.RUNS}
        missing.update(lacking)
        if args.verbose:
            note = ", ".join(f"{name(op)} x{c}" for op, c in sorted(lacking.items())) or "nothing missing"
            print(f"  event 0x{n:03X}: {len(code)} bytes, {sum(upgrade.values())} upgrade instruction(s); {note}")
    print(f"{events} event(s) use the upgrade's instructions"
          + (f" ({sum(totals.values())} uses)" if totals else ""))
    if missing:
        print("Instructions the upgrade doesn't run yet: "
              + ", ".join(f"{name(op)} (0x{op:02X}) x{c}" for op, c in missing.most_common()))
    else:
        print("Every upgrade instruction they use runs.")
    if routines:
        print("Routine (0xCF) calls into the hack's own code, each needing a C# port (docs/API.md, AddRoutine): "
              + ", ".join(f"0x{a:08X} x{c}" for a, c in routines.most_common()))


def cmd_patch(args: argparse.Namespace) -> None:
    patch = eiu.read_ppf(args.ppf)
    try:
        eiu.apply_ppf(patch, args.disc, args.output)
    except ValueError as e:
        sys.exit(str(e))
    print(f"Wrote {args.output} ({patch.description or 'no description'})")


def cmd_extract(args: argparse.Namespace) -> None:
    with eiu.Disc(args.disc) as disc:
        entry = disc.find(args.path)
        if entry is None or entry.is_dir:
            sys.exit(f"no file {args.path!r} on the disc (try EVENT/TEST.EVT or BATTLE.BIN)")
        Path(args.output).write_bytes(disc.read(entry))
    print(f"Wrote {args.output} ({entry.size:,} bytes)")


def main() -> None:
    parser = argparse.ArgumentParser(prog="eiu.py", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True, metavar="command")

    p = sub.add_parser("disasm", help="a .e file as a readable script (.evt)")
    p.add_argument("file", help="a .e file (or, with --ps1, PS1 event code)")
    p.add_argument("-o", "--output", help="where to write the script (default: beside it, as .evt)")
    p.add_argument("--ps1", action="store_true", help="the PS1's layout of the message instructions")
    p.add_argument("--offsets", action="store_true", help="each line's offset, as the upgrade's log gives them")
    p.set_defaults(run=cmd_disasm)

    p = sub.add_parser("build", help="a script (.evt) as a .e file")
    p.add_argument("file", help="the script")
    p.add_argument("-o", "--output", help="where to write the event (default: beside it, as .e)")
    p.add_argument("--ps1", action="store_true", help="the PS1's layout of the message instructions (and no marker)")
    p.add_argument("--no-marker", action="store_true", help="leave out the upgrade's marker (a remaster event)")
    p.set_defaults(run=cmd_build)

    for command, helped, run in (("convert", "a PS1 event as the remaster's: .e, .evt and .lines.txt", cmd_convert),
                                 ("scan", "what a PS1 disc's events use, and what the upgrade doesn't run yet", cmd_scan)):
        p = sub.add_parser(command, help=helped)
        p.add_argument("--disc", help="a PS1 disc image (.bin, 2352-byte sectors), the hack applied")
        p.add_argument("--test-evt", help="instead of --disc: its EVENT/TEST.EVT")
        p.add_argument("--battle-bin", help="with --test-evt: its BATTLE.BIN, for the hack's own instruction lengths")
        p.set_defaults(run=run)
        if command == "convert":
            p.add_argument("--event", type=number, required=True, help="the PS1 event's number (0x6E or 110)")
            p.add_argument("--first-line", type=number, help="the remaster line id its message 1 shows")
            p.add_argument("--as", dest="as_number", type=number, help="the event number to write it as")
            p.add_argument("-o", "--output", default="converted", help="the folder to write to (default: converted)")
        else:
            p.add_argument("--events", type=number, nargs="+", help="only these events")
            p.add_argument("--all", action="store_true", help="count events that don't use the upgrade too")
            p.add_argument("-v", "--verbose", action="store_true", help="a line per event")

    p = sub.add_parser("patch", help="apply a PPF patch to a copy of a disc image")
    p.add_argument("ppf")
    p.add_argument("disc", help="the disc the patch is for (it's not changed)")
    p.add_argument("output", help="the patched copy")
    p.set_defaults(run=cmd_patch)

    p = sub.add_parser("extract", help="copy a file off a PS1 disc image")
    p.add_argument("disc")
    p.add_argument("path", help="its path on the disc, e.g. EVENT/TEST.EVT")
    p.add_argument("output")
    p.set_defaults(run=cmd_extract)

    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
