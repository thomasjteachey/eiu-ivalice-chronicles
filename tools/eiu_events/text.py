"""A PS1 event's text: its messages, numbered as its DisplayMessages name them, decoded to read.

FFT's PS1 text has its own encoding. A message ends with 0xFE (closed by a button) or 0xFF (closed by the event);
0xF8 is a line break; 0xE0 is the hero's name; 0xFB ... 0xFC is a list of answers (a menu); some control codes take
the byte after them (0xE2 a wait, 0xE3 a colour or a speaker's name, ...), and 0xD0-0xDA start two-byte characters.

decode() spells out what it knows and shows the rest as {XX} tokens (a code and its byte as {E3:08}), so nothing is
lost and nothing is guessed. A hack with its own font (Ivalice ReMixed's has parentheses at 0x8D and 0x8E) reads
right after a fix to CHARS.
"""

from __future__ import annotations

MESSAGE_ENDS = (0xFE, 0xFF)
CODES_WITH_A_BYTE = (0xE2, 0xE3, 0xE6, 0xE8, 0xF5, 0xF6)
TWO_BYTE = range(0xD0, 0xDB)
LINE_BREAK, HERO, MENU_START, MENU_END = 0xF8, 0xE0, 0xFB, 0xFC

# One-byte characters: digits, A-Z, a-z and the punctuation known so far (vanilla's font).
CHARS: dict[int, str] = {i: str(i) for i in range(10)}
CHARS.update({0x0A + i: chr(65 + i) for i in range(26)})
CHARS.update({0x24 + i: chr(97 + i) for i in range(26)})
CHARS.update({0xFA: " ", 0x3E: "!", 0x40: "?", 0x42: "+", 0x44: "/", 0x46: ":", 0x5F: ".", 0x8D: "-", 0x93: "'",
              0x91: '"'})
# Two-byte characters known so far.
PAIRS: dict[bytes, str] = {b"\xda\x76": "'", b"\xd9\xb6": ".", b"\xd9\xc1": ",", b"\xda\x74": ",", b"\xd1\x1d": "-",
                           b"\xd1\x29": "*", b"\xd1\x17": "—", b"\xda\x75": ";", b"\xd9\xb8": "%",
                           b"\xd9\xb7": "&", b"\xda\x73": " "}
NAMED_CODES = {HERO: "{hero}", LINE_BREAK: "\n", MENU_START: "{menu}", MENU_END: "{/menu}", 0xFD: "{keep-open}"}


def split_messages(text: bytes) -> list[bytes]:
    """An event's text as its messages (each with its end), numbered from 1 as its DisplayMessages name them."""
    messages, at, start = [], 0, 0
    text = text.rstrip(b"\0")
    while at < len(text):
        b = text[at]
        if b in CODES_WITH_A_BYTE or b in TWO_BYTE:
            at += 2
            continue
        at += 1
        if b in MESSAGE_ENDS:
            messages.append(text[start:at])
            start = at
    if text[start:]:
        messages.append(text[start:])
    return messages


def decode(message: bytes) -> str:
    """A message as text: what's known spelled out, the rest as {XX} or {XX:YY} tokens; its end left off."""
    out, at = [], 0
    while at < len(message):
        b = message[at]
        if b in MESSAGE_ENDS:
            break
        if b in TWO_BYTE:
            pair = bytes(message[at:at + 2])
            out.append(PAIRS.get(pair, "{" + pair.hex(":").upper() + "}"))
            at += 2
        elif b in CODES_WITH_A_BYTE:
            out.append("{" + bytes(message[at:at + 2]).hex(":").upper() + "}")
            at += 2
        else:
            out.append(CHARS.get(b) or NAMED_CODES.get(b) or f"{{{b:02X}}}")
            at += 1
    return "".join(out)


def menus(messages: list[bytes]) -> set[int]:
    """The numbers of the messages that are menus (a list of answers)."""
    return {n for n, m in enumerate(messages, 1) if MENU_START in m}
