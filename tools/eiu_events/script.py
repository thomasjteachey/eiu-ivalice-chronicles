"""Event code as readable scripts, and back.

A script has one instruction a line, by its wiki name, with its parameters as numbers:

    If()
        SET(0x0010, 0x0001)        # var 0x10 = 1
    Else()
        SET(0x0010, 0x0002)
    EndIf()
    DisplayMessage(0x10, 0x11, 0x0004CF18, 0x85, 0x00, 0x00, 0, 0, 0, 0x00, 0x00)
    EventEnd()

- Numbers are hex (0x1F) or decimal (31, -4); the disassembler writes each kind of parameter its own way (see
  instructions.py), and either works anywhere.
- Names don't care about case. An opcode with no name is OpXX (Op23). Bytes(0x12, 0x34) puts raw bytes in.
- Indentation, blank lines and # comments are for people; the assembler ignores them.

disassemble() then assemble() gives back the same bytes, whatever the code holds.
"""

from __future__ import annotations

import re

from .instructions import (BY_NAME, MARKER, NAMES, PS1_LENGTHS, REMASTER_LENGTHS, WIDTHS, kinds, name)

# Instructions that open a block (indent what follows), close one (dedent themselves), or both.
OPENS = {0x01, 0x05, 0x0A, 0x2A}            # If, Switch, DefineSub, BlockStart
CLOSES = {0x04, 0x09, 0x0B, 0x2B}           # EndIf, EndSwitch, EndSub, BlockEnd
MIDDLES = {0x02, 0x03, 0x06, 0x07}          # ElseIf, Else, Case, Default: back a step for themselves only
SPACED = {0x0A, 0x0E}                       # DefineSub, GoToTarget: a blank line before them


def _format(value: int, kind: str) -> str:
    if kind == "b":
        return f"0x{value:02X}"
    if kind == "w":
        return f"0x{value:04X}"
    if kind == "d" or (kind == "L" and value == 0xFFFFFFFF):  # no line: 0xFFFFFFFF
        return f"0x{value:08X}"
    return str(value)


def _read(code: bytes, at: int, kind: str) -> int:
    width = WIDTHS[kind]
    return int.from_bytes(code[at:at + width], "little", signed=kind in "cs")


def disassemble(code: bytes, remaster: bool = True, lengths: bytes | None = None, offsets: bool = False,
                header: str | None = None) -> str:
    """Event code as a script. remaster: the remaster's layout of the two message instructions (a .e file), else the
    PS1's. lengths: the PS1 length table (the default is the upgrade's, as ReMixed has it). An upgrade event's marker
    is left out (assemble() puts it back). offsets: each line ends with the instruction's offset as the upgrade's log
    gives it (the file's offset + 4, where the game puts the code)."""
    table = (REMASTER_LENGTHS if lengths is None else _widened(lengths)) if remaster else (lengths or PS1_LENGTHS)
    lines = [f"# {header}"] if header else []
    at = 0
    if remaster:
        if code.startswith(MARKER):
            at = len(MARKER)
            lines.append("# An upgrade event: `eiu.py build` puts the marker (0E 2C 0E 3C) back in front.")
        else:
            lines.append("# Not an upgrade event (no marker): build it with --no-marker to keep it the remaster's own.")
    depth = 0
    while at < len(code):
        op = code[at]
        size = 1 + table[op]
        if at + size > len(code):  # a last instruction cut short: raw bytes
            lines.append("    " * depth + "Bytes(" + ", ".join(f"0x{b:02X}" for b in code[at:]) + ")")
            break
        if op in CLOSES or op in MIDDLES:
            depth = max(0, depth - 1)
        if op in SPACED and lines and lines[-1].strip():
            lines.append("")
        params, p = [], at + 1
        for kind in kinds(op, table, remaster):
            params.append(_format(_read(code, p, kind), kind))
            p += WIDTHS[kind]
        text = "    " * depth + f"{name(op)}({', '.join(params)})"
        if offsets:
            text = f"{text:<60} # @0x{at + 4:04X}"
        lines.append(text)
        if op in OPENS or op in MIDDLES:
            depth += 1
        at += size
    return "\n".join(lines) + "\n"


def _widened(lengths: bytes) -> bytes:
    from .instructions import remaster_lengths
    return remaster_lengths(lengths)


class ScriptError(ValueError):
    """A line of a script the assembler can't read; says which and why."""


_LINE = re.compile(r"^(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\((?P<args>.*)\))?$")


def assemble(script: str, remaster: bool = True, lengths: bytes | None = None, marker: bool = True) -> bytes:
    """A script as event code. remaster: the remaster's layout of the message instructions (for a .e file).
    marker: put the upgrade's marker in front (unless the script already starts with it)."""
    table = (REMASTER_LENGTHS if lengths is None else _widened(lengths)) if remaster else (lengths or PS1_LENGTHS)
    out = bytearray()
    for number, raw in enumerate(script.splitlines(), 1):
        text = raw.split("#", 1)[0].strip()
        if not text:
            continue
        match = _LINE.match(text)
        if not match:
            raise ScriptError(f"line {number}: {text!r} isn't Name(parameters)")
        word, args = match["name"], match["args"]
        try:
            values = [_number(a) for a in args.split(",")] if args and args.strip() else []
        except ValueError:
            raise ScriptError(f"line {number}: {args!r} has something that isn't a number") from None
        if word.lower() == "bytes":
            if any(not 0 <= v <= 0xFF for v in values):
                raise ScriptError(f"line {number}: Bytes() takes bytes (0 to 0xFF)")
            out += bytes(values)
            continue
        op = _opcode(word)
        if op is None:
            raise ScriptError(f"line {number}: no instruction called {word} (see docs/EVENT-TOOLS.md)")
        spec = kinds(op, table, remaster)
        if len(values) != len(spec):
            raise ScriptError(f"line {number}: {name(op)} takes {len(spec)} parameter(s), not {len(values)}")
        out.append(op)
        for value, kind in zip(values, spec):
            width, signed = WIDTHS[kind], kind in "cs"
            low, high = (-(1 << (8 * width - 1)), (1 << (8 * width - 1)) - 1) if signed else (0, (1 << (8 * width)) - 1)
            if signed and value > high and value < (1 << (8 * width)):  # a signed one written in hex: its bits
                value -= 1 << (8 * width)
            if not signed and value < 0 and value >= -(1 << (8 * width - 1)):  # an unsigned one written negative
                value += 1 << (8 * width)
            if not low <= value <= high:
                raise ScriptError(f"line {number}: {value} doesn't fit {name(op)}'s parameter ({width} byte(s))")
            out += value.to_bytes(width, "little", signed=signed)
    code = bytes(out)
    if marker and remaster and not code.startswith(MARKER):
        code = MARKER + code
    return code


def _number(text: str) -> int:
    """0x1F, 31, -4, or a decimal with leading zeros (007)."""
    text = text.strip()
    return int(text, 10) if re.fullmatch(r"-?\d+", text) else int(text, 0)


def _opcode(word: str) -> int | None:
    if word.lower() in BY_NAME:
        return BY_NAME[word.lower()]
    match = re.fullmatch(r"[Oo][Pp]([0-9A-Fa-f]{2})", word)
    return int(match[1], 16) if match else None


__all__ = ["disassemble", "assemble", "ScriptError", "NAMES"]
