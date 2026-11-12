"""Tests for tools/eiu_events. Run from the repo's root:

    python -I -m unittest discover -s tests
"""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))

import eiu_events as eiu  # noqa: E402
from eiu_events.instructions import NAMES, REMASTER_NAMES, WIDTHS  # noqa: E402

SCRIPT = """
# a small event
If()
    SET(0x0010, 0x0001)
Else()
    SET(0x0010, 2)            # decimal works too
EndIf()
Switch(0x05)
    Case(0x0007)
        Random(0x15, 100)
    Break()
EndSwitch()
UnitAddress(0x85, 0x70)
LoadAddress(0x15, 0x0024, 0x70, 1)
Camera(-256, 0, 512, 0, 0, 0, 4096, 60)
DisplayMessage(0x10, 0x11, 315160, 0x85, 0x00, 0x00, 0, 0, 0, 0x02, 0x00)
ChangeDialog(0x01, 0xFFFFFFFF, 0x00, 0x00)
Routine(0x80151000)
Bytes(0x00)
EventEnd()
"""


class Scripts(unittest.TestCase):
    def test_round_trip(self):
        code = eiu.assemble(SCRIPT)
        self.assertTrue(code.startswith(eiu.MARKER))
        self.assertEqual(eiu.assemble(eiu.disassemble(code)), code)

    def test_every_byte_string_round_trips(self):
        # Whatever the bytes (a cut-off last instruction included), they come back.
        for data in (bytes(range(256)), bytes([0x10, 0x00]), eiu.MARKER + bytes([0xC1, 1, 2])):
            script = eiu.disassemble(data)
            self.assertEqual(eiu.assemble(script, marker=data.startswith(eiu.MARKER)), data)

    def test_readable(self):
        script = eiu.disassemble(eiu.assemble(SCRIPT))
        self.assertIn("    SET(0x0010, 0x0001)", script)  # indented inside the If
        self.assertIn("Camera(-256, 0, 512, 0, 0, 0, 4096, 60)", script)  # signed values in decimal
        self.assertIn("DisplayMessage(0x10, 0x11, 315160,", script)  # a line id in decimal

    def test_errors_name_the_line(self):
        with self.assertRaisesRegex(eiu.ScriptError, "line 2: SET takes 2"):
            eiu.assemble("If()\nSET(1)\n")
        with self.assertRaisesRegex(eiu.ScriptError, "line 1: no instruction called Nope"):
            eiu.assemble("Nope()")
        with self.assertRaisesRegex(eiu.ScriptError, "doesn't fit"):
            eiu.assemble("PlayTrack(0x100)")

    def test_layouts_fit_the_length_tables(self):
        for op, (_, spec) in NAMES.items():
            self.assertEqual(sum(WIDTHS[k] for k in spec), eiu.PS1_LENGTHS[op], NAMES[op][0])
        for op in (0x10, 0x51):
            self.assertEqual(sum(WIDTHS[k] for k in REMASTER_NAMES[op][1]), eiu.REMASTER_LENGTHS[op])


class Conversion(unittest.TestCase):
    # PS1: AlterParameter writing a byte at parameter 5 (the unit, past the message number); DisplayMessage of
    # message 2 (style 0x11); DisplayMessage of message 3 as a menu; ChangeDialog to message 4; EventEnd.
    PS1 = (bytes([0xE6, 0x05, 0x70])
           + bytes([0x10, 0x10, 0x11, 0x02, 0x00, 0x85, 0, 0, 0, 0, 0, 0, 0, 0, 0x02])
           + bytes([0x10, 0x10, 0x11, 0x03, 0x00, 0x85, 0, 0, 0, 0, 0, 0, 0, 0, 0x03])
           + bytes([0x51, 0x01, 0x04, 0x00, 0x00, 0x00])
           + bytes([0xDB]))

    def test_layout(self):
        code, named, template = eiu.remaster_layout(self.PS1, first_line=1000, menus={3})
        self.assertEqual(code[1], 0x05 + 2)  # AlterParameter's offset moved past the wider line id
        self.assertEqual(int.from_bytes(code[3 + 3:3 + 7], "little"), 1001)  # message 2 -> line 1000 + 2 - 1
        self.assertEqual(named, {2, 3, 4})
        self.assertEqual(template[:3], bytes([0x10, 0x10, 0x11]))
        menu = code[3 + 18:3 + 36]
        self.assertEqual(menu[2] & 0xF, 0x4 | 1)  # a menu's kind
        self.assertEqual(menu[16], 0)  # a menu closes as 0
        self.assertEqual(len(code), len(self.PS1) + 3 + 3 + 2)

    def test_shadow_messages_cover_unnamed_lines(self):
        converted, named, template = eiu.remaster_layout(self.PS1, first_line=1000, menus={3})
        shadows = eiu.shadow_messages(self.PS1, eiu.PS1_LENGTHS, template, 1000, 5, named)
        self.assertEqual(shadows[0], 0x0D)  # a GoTo over them
        ids = [int.from_bytes(shadows[2 + 18 * k + 3:2 + 18 * k + 7], "little") for k in range(2)]
        self.assertEqual(ids, [1000, 1004])  # messages 1 and 5, which no DisplayMessage names
        full = eiu.to_remaster(self.PS1, first_line=1000, messages=5, menus={3})
        self.assertEqual(full, shadows + converted)

    def test_no_text_names_no_line(self):
        code, _, _ = eiu.remaster_layout(self.PS1)
        self.assertEqual(int.from_bytes(code[3 + 3:3 + 7], "little"), eiu.NO_LINE)

    def test_event_code_from_test_evt(self):
        block = bytearray(0x2000)
        block[0:4] = (4 + 3).to_bytes(4, "little")  # its text starts after 3 bytes of code
        block[4:7] = bytes([0x0E, 0x01, 0xDB])
        block[7:10] = bytes([0x2B, 0x24, 0xFE])  # two characters and the message's end
        test_evt = bytes(0x2000) + bytes(block)
        self.assertEqual(eiu.event_code(test_evt, 1), bytes([0x0E, 0x01, 0xDB]))
        self.assertEqual(eiu.split_messages(eiu.event_text(test_evt, 1)), [bytes([0x2B, 0x24, 0xFE])])

    def test_event_file_checks_its_size(self):
        self.assertEqual(eiu.event_file(bytes([0xDB])), eiu.MARKER + bytes([0xDB]))
        with self.assertRaises(ValueError):
            eiu.event_file(bytes(eiu.MAX_CODE))


class Text(unittest.TestCase):
    def test_decode(self):
        # "Hi" , line break, the hero's name, an unknown code with its byte, an unknown two-byte character
        message = bytes([0x11, 0x2C, 0xF8, 0xE0, 0xE3, 0x08, 0xD9, 0xC7, 0xFE])
        self.assertEqual(eiu.decode(message), "Hi\n{hero}{E3:08}{D9:C7}")

    def test_split_and_menus(self):
        text = bytes([0x11, 0xFE, 0xFB, 0x01, 0xF8, 0x02, 0xFC, 0xFF, 0x00, 0x00])
        messages = eiu.split_messages(text)
        self.assertEqual(len(messages), 2)
        self.assertEqual(eiu.menus(messages), {2})


if __name__ == "__main__":
    unittest.main()
