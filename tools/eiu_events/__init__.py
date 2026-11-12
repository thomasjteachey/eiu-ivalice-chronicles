"""Event tools for the Event Instruction Upgrade: readable event scripts, and PS1 events converted for the remaster.

The command line is tools/eiu.py (docs/EVENT-TOOLS.md); this package is the same as a library for build scripts:

    import sys; sys.path.insert(0, "path/to/eiu-ivalice-chronicles/tools")
    import eiu_events

    script = eiu_events.disassemble(Path("event110.e").read_bytes())
    code = eiu_events.assemble(script)
    remaster = eiu_events.to_remaster(eiu_events.event_code(test_evt, 0x6E), first_line=315160, messages=12)

Python 3.11+, standard library only.
"""

from .convert import (NO_LINE, event_code, event_count, event_file, event_text, message_style, remaster_layout,
                      shadow_messages, to_remaster)
from .disc import Disc
from .instructions import (MARKER, MAX_CODE, NAMES, PS1_LENGTHS, REMASTER_LENGTHS, RUNS, UPGRADE, WIDENED,
                           read_lengths, remaster_lengths)
from .ppf import apply_ppf, read_ppf
from .script import ScriptError, assemble, disassemble
from .text import decode, menus, split_messages

__all__ = ["NO_LINE", "event_code", "event_count", "event_file", "event_text", "message_style", "remaster_layout",
           "shadow_messages", "to_remaster", "Disc", "MARKER", "MAX_CODE", "NAMES", "PS1_LENGTHS", "REMASTER_LENGTHS",
           "RUNS", "UPGRADE", "WIDENED", "read_lengths", "remaster_lengths", "apply_ppf", "read_ppf", "ScriptError",
           "assemble", "disassemble", "decode", "menus", "split_messages"]
