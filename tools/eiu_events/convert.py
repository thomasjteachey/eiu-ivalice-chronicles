"""PS1 events into the remaster's: an event's code out of a PS1 disc's TEST.EVT, its messages in the remaster's layout
pointed at lines of text, and what the remaster needs to size its text boxes.

    code = event_code(test_evt, 0x6E)                        # the PS1 event's code (no text)
    remaster = to_remaster(code, first_line=315160, messages=12, menus={4})
    Path("event110.e").write_bytes(event_file(remaster))     # the marker in front, the size checked

Steps a mod wants to do its own way have a hook (restyle) or can be called one by one (remaster_layout,
shadow_messages), as Ivalice ReMixed's build does.
"""

from __future__ import annotations

from collections.abc import Callable

from .instructions import MARKER, MAX_CODE, PS1_LENGTHS, WIDENED

EVENT_BLOCK = 0x2000  # each event's block in TEST.EVT: a u32 (where its text starts), its code, its text
NO_LINE = 0xFFFFFFFF  # a message that names no line


def event_count(test_evt: bytes) -> int:
    return len(test_evt) // EVENT_BLOCK


def event_code(test_evt: bytes, number: int, lengths: bytes = PS1_LENGTHS) -> bytes:
    """Event number's code from a PS1 TEST.EVT: up to its text, or with no text (an offset past the block) up to its
    first EventEnd (0xDB) at an instruction boundary. ValueError if it has neither."""
    block = test_evt[number * EVENT_BLOCK:(number + 1) * EVENT_BLOCK]
    if len(block) < 8:
        raise ValueError(f"TEST.EVT has no event 0x{number:X}")
    text = int.from_bytes(block[:4], "little")
    if 4 <= text <= EVENT_BLOCK:
        return bytes(block[4:text])
    at = 4
    while block[at] != 0xDB:
        at += 1 + lengths[block[at]]
        if at >= len(block):
            raise ValueError(f"event 0x{number:X} has no EventEnd")
    return bytes(block[4:at + 1])


def event_text(test_evt: bytes, number: int) -> bytes:
    """Event number's text (its messages, in the PS1's encoding), or b"" when it has none."""
    block = test_evt[number * EVENT_BLOCK:(number + 1) * EVENT_BLOCK]
    text = int.from_bytes(block[:4], "little") if len(block) >= 4 else 0
    return bytes(block[text:]) if 4 <= text < EVENT_BLOCK else b""


# A DisplayMessage's box style (the byte after its type): the high nibble places the box, the low its kind, whose low
# two bits are the box (0-3, as ChangeDialog names it). The remaster draws only the places and kinds vanilla's events
# use (another place crashed it), and gives a menu (a line with a list of answers) kind 4.
VANILLA_MESSAGE_PLACES = {0x0, 0x1, 0x7, 0x9}
VANILLA_MESSAGE_KINDS = {0x0, 0x1, 0x2, 0x3, 0x5, 0x9, 0xA, 0xB}
MENU_KIND = 0x4


def message_style(style: int, menu: bool = False) -> int:
    """A box style the remaster can draw: an unknown place becomes 0 (vanilla's centred boxes), an unknown kind keeps
    its low two bits (or 1); a menu's kind is its box with MENU_KIND."""
    place, kind = style >> 4, style & 0xF
    if place not in VANILLA_MESSAGE_PLACES:
        place = 0
    if kind not in VANILLA_MESSAGE_KINDS:
        kind = kind & 3 or 1
    if menu:
        kind = kind & 3 | MENU_KIND
    return place << 4 | kind


# restyle(style, placed, menu) -> (style, placed, centred): a mod's own choice of a DisplayMessage's box (its style
# byte; placed, its 9 bytes of unit, two bytes, x, y and arrow; whether it's a menu), and the last byte (centring).
Restyle = Callable[[int, bytes, bool], tuple[int, bytes, int]]


def remaster_layout(code: bytes, lengths: bytes = PS1_LENGTHS, first_line: int | None = None,
                    menus: set[int] | frozenset[int] = frozenset(), restyle: Restyle | None = None
                    ) -> tuple[bytes, set[int], bytes | None]:
    """PS1 event code with its DisplayMessage (0x10) and ChangeDialog (0x51) in the remaster's layout: message n names
    line first_line + n - 1 (none without a first_line), a box style the remaster draws (message_style; menus: the
    message numbers that are menus), and an AlterParameter (0xE6) aimed past the message number moved 2 bytes on.
    Returns the code, the message numbers it names, and its first DisplayMessage that isn't a menu (a template for
    shadow_messages)."""
    out, at, patches, named, template = bytearray(), 0, [], set(), None

    def line(message: int) -> int:
        return NO_LINE if first_line is None or message == 0xFFFF else first_line + message - 1

    while at < len(code):
        op = code[at]
        ins = bytes(code[at:at + 1 + lengths[op]])
        at += len(ins)
        if op == 0xE6:  # AlterParameter: the next instruction past other 0xE6s
            patches.append(len(out))
            out += ins
            continue
        if op in WIDENED and len(ins) == 1 + lengths[op]:
            p = ins[1:]
            if op == 0x10:
                message = p[2] | p[3] << 8
                menu = message in menus
                style, placed, centred = p[1], p[4:13], 0  # placed: the unit, two bytes, x, y, the arrow
                if restyle is not None:
                    style, placed, centred = restyle(style, placed, menu)
                ins = (bytes([op, p[0], message_style(style, menu)]) + line(message).to_bytes(4, "little") + placed
                       + bytes([0 if menu else p[13], centred]))
                if not menu:
                    template = template or ins
            else:
                message = p[1] | p[2] << 8
                ins = bytes([op, p[0]]) + line(message).to_bytes(4, "little") + p[3:5]
            named.add(message)
            for k in patches:
                if out[k + 1] & 0x3F >= WIDENED[op]:
                    out[k + 1] += 2
        patches = []
        out += ins
    return bytes(out), named, template


def shadow_messages(code: bytes, lengths: bytes, template: bytes | None, first_line: int | None, count: int,
                    named: set[int], skip: set[int] | frozenset[int] = frozenset()) -> bytes:
    """What goes in front of a converted event's code so the remaster sizes every box: a GoTo over a DisplayMessage
    for each of its lines no DisplayMessage names (lines its code picks as it runs, through AlterParameter), never
    run, to a label the code (the PS1 layout) doesn't use. When an event loads, the remaster scans its code to the
    first EventEnd and sizes a box to the lines each DisplayMessage names; a line it doesn't find there gets a box of no
    width. Empty when there are none (skip: lines that need no box)."""
    missing = [n for n in range(1, count + 1) if n not in named and n not in skip]
    if not missing or template is None or first_line is None:
        return b""
    used, at = {0x2C, 0x3C}, 0
    while at < len(code):
        if code[at] == 0x0E:
            used.add(code[at + 1])
        at += 1 + lengths[code[at]]
    label = max(k for k in range(0x100) if k not in used)
    shadows = b"".join(template[:3] + (first_line + n - 1).to_bytes(4, "little") + template[7:] for n in missing)
    return bytes([0x0D, label]) + shadows + bytes([0x0E, label])


def to_remaster(code: bytes, lengths: bytes = PS1_LENGTHS, first_line: int | None = None, messages: int = 0,
                menus: set[int] | frozenset[int] = frozenset(), restyle: Restyle | None = None) -> bytes:
    """PS1 event code as the remaster's: remaster_layout(), then shadow_messages() for its messages (how many it has)."""
    converted, named, template = remaster_layout(code, lengths, first_line, menus, restyle)
    return shadow_messages(code, lengths, template, first_line, messages, named) + converted


def event_file(code: bytes) -> bytes:
    """A .e file's bytes: the marker in front of the remaster-layout code. ValueError if it's too long for the
    upgrade (MAX_CODE)."""
    data = code if code.startswith(MARKER) else MARKER + code
    if len(data) > MAX_CODE:
        raise ValueError(f"the event is 0x{len(data):X} bytes; the upgrade takes up to 0x{MAX_CODE:X}")
    return data
