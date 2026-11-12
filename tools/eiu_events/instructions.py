"""The event instructions: each opcode's name, its parameters, and how long it is.

The names are the FFHacktics wiki's (https://ffhacktics.com/wiki/Event_Instructions), for vanilla's instructions and
the Event Instruction Upgrade's alike. Each instruction's parameters are a string of kinds, one letter a parameter:

    b  a byte, written in hex          B  a byte, written in decimal       c  a signed byte
    w  a halfword (u16), in hex        W  a halfword, in decimal           s  a signed halfword
    d  a word (u32), in hex            L  a line of text's id (u32), in decimal, as the game's text files number them

PS1 events and the remaster's differ in two instructions: the remaster names a line of text with a 32-bit id where
the PS1 had a 16-bit message number, so DisplayMessage (0x10) and ChangeDialog (0x51) are longer
(docs/WRITING-EVENTS.md, "Messages"). Everything else is the same length in both.
"""

from __future__ import annotations

# What an upgrade event's code starts with: GoToTarget 0x2C, GoToTarget 0x3C. The upgrade runs only such events.
MARKER = bytes([0x0E, 0x2C, 0x0E, 0x3C])
# How long an event's code can be, marker included.
MAX_CODE = 0x2000

# Parameter bytes per opcode in a PS1 event, as the PS1 hack's interpreter reads them (its BATTLE.BIN 0x8014D170, as
# The Lion War ReMixed carries the Event Instruction Upgrade). A hack with another version of the upgrade has its own
# table in its BATTLE.BIN: read_lengths() reads it.
PS1_LENGTHS = bytes.fromhex(
    "000001000001020000000100010101030E050202010000010610050E01000005"
    "0602030300040200080200000706080400050705020402020200000C02020904"
    "0504000102030208000000000201030101050207020200000301010103010106"
    "02030601020000020308050502020C020402000E060606000204020200010404"
    "0303000501010304000000000603000103030502020000020202000201030203"
    "0000000000000000000000000404040404040404040404040404040404040203"
    "1005020205020303000000000103020401010101010100000101000000000000"
    "0C01010001020203050504040102020200020000000000000000000000000000")

# The two instructions whose layout the remaster widened, and the first PS1 parameter that moves 2 bytes on (the
# message number becomes a u32 line id).
WIDENED = {0x10: 4, 0x51: 3}


def remaster_lengths(ps1: bytes = PS1_LENGTHS) -> bytes:
    """A PS1 length table with the remaster's message layout (DisplayMessage 3 bytes longer, ChangeDialog 2)."""
    lengths = bytearray(ps1)
    lengths[0x10] += 3
    lengths[0x51] += 2
    return bytes(lengths)


REMASTER_LENGTHS = remaster_lengths()

BATTLE_BIN_BASE = 0x80067000  # where BATTLE.BIN loads on the PS1
LENGTHS_IN_BATTLE_BIN = 0x8014D170 - BATTLE_BIN_BASE


def read_lengths(battle_bin: bytes) -> bytes:
    """A PS1 hack's own length table, from its BATTLE.BIN."""
    return bytes(battle_bin[LENGTHS_IN_BATTLE_BIN:LENGTHS_IN_BATTLE_BIN + 256])


NAMES: dict[int, tuple[str, str]] = {
    0x00: ('Not', ''),
    0x01: ('If', ''),
    0x02: ('ElseIf', 'b'),
    0x03: ('Else', ''),
    0x04: ('EndIf', ''),
    0x05: ('Switch', 'b'),
    0x06: ('Case', 'w'),
    0x07: ('Default', ''),
    0x08: ('Break', ''),
    0x09: ('EndSwitch', ''),
    0x0A: ('DefineSub', 'b'),
    0x0B: ('EndSub', ''),
    0x0C: ('CallSub', 'b'),
    0x0D: ('GoTo', 'b'),
    0x0E: ('GoToTarget', 'b'),
    0x0F: ('ExitTo', 'bbb'),
    0x10: ('DisplayMessage', 'bbwbbbsssb'),
    0x11: ('UnitAnim', 'bbwb'),
    0x13: ('ChangeMapBeta', 'Bb'),
    0x14: ('PlayTrack', 'b'),
    0x15: ('ResetUnitCalculations', ''),
    0x16: ('Pause', ''),
    0x17: ('GraphicTimeout', 'b'),
    0x18: ('Effect', 'wbBBb'),
    0x19: ('Camera', 'ssssssss'),
    0x1A: ('MapDarkness', 'bcccB'),
    0x1B: ('MapLight', 'sssssss'),
    0x1C: ('EventSpeed', 'b'),
    0x1D: ('CameraFusionStart', ''),
    0x1E: ('CameraFusionEnd', ''),
    0x1F: ('Focus', 'bbbbb'),
    0x21: ('SoundEffect', 'w'),
    0x22: ('SwitchTrack', 'bcB'),
    0x27: ('ReloadMapState', ''),
    0x28: ('WalkTo', 'bbBBbbcb'),
    0x29: ('WaitWalk', 'bb'),
    0x2A: ('BlockStart', ''),
    0x2B: ('BlockEnd', ''),
    0x2C: ('FaceUnit2', 'bbbbbbb'),
    0x2D: ('RotateUnit', 'bbbbbb'),
    0x2E: ('Background', 'BBBBBBBb'),
    0x2F: ('BlockLoop', 'Bbw'),
    0x31: ('ColorBGBeta', 'bcccB'),
    0x32: ('ColorUnit', 'bbbcccB'),
    0x33: ('ColorField', 'bcccB'),
    0x38: ('FocusSpeed', 's'),
    0x3B: ('SpriteMove', 'bbsssbbs'),
    0x3C: ('Weather', 'bb'),
    0x3D: ('RemoveUnit', 'bb'),
    0x3E: ('ColorScreen', 'bBBBBBBs'),
    0x41: ('EarthquakeStart', 'BBBB'),
    0x42: ('EarthquakeEnd', ''),
    0x43: ('CallFunction', 'b'),
    0x44: ('Draw', 'bb'),
    0x45: ('AddUnit', 'bbb'),
    0x46: ('Erase', 'bb'),
    0x47: ('AddGhostUnit', 'bbbBBbbb'),
    0x48: ('WaitAddUnit', ''),
    0x49: ('AddUnitStart', ''),
    0x4A: ('AddUnitEnd', ''),
    0x4B: ('WaitAddUnitEnd', ''),
    0x4C: ('ChangeMap', 'Bb'),
    0x4D: ('Reveal', 'B'),
    0x4E: ('UnitShadow', 'bbb'),
    0x4F: ('SetDaytime', 'b'),
    0x50: ('PortraitCol', 'b'),
    0x51: ('ChangeDialog', 'bwbb'),
    0x53: ('FaceUnit', 'bbbbbbb'),
    0x54: ('Use3DObject', 'bb'),
    0x55: ('UseFieldObject', 'bb'),
    0x56: ('Wait3DObject', ''),
    0x57: ('WaitFieldObject', ''),
    0x58: ('LoadEVTCHR', 'bbb'),
    0x59: ('SaveEVTCHR', 'b'),
    0x5A: ('SaveEVTCHRClear', 'b'),
    0x5B: ('LoadEVTCHRClear', 'b'),
    0x5E: ('EndTrack', 'b'),
    0x5F: ('WarpUnit', 'bbBBbb'),
    0x60: ('FadeSound', 'bB'),
    0x62: ('SetCoordinates', 'wBBbb'),
    0x63: ('CameraSpeedCurve', 'b'),
    0x64: ('WaitRotateUnit', 'bb'),
    0x65: ('WaitRotateAll', ''),
    0x68: ('MirrorSprite', 'bbb'),
    0x69: ('FaceTile', 'bbBBbbbb'),
    0x6A: ('EditBGSound', 'bccbb'),
    0x6B: ('BGSound', 'bccbB'),
    0x6E: ('SpriteMoveBeta', 'bbsssbbs'),
    0x6F: ('WaitSpriteMove', 'bb'),
    0x70: ('Jump', 'bbBb'),
    0x71: ('SetFirstMiscUnit', 'bb'),
    0x76: ('DarkScreen', 'bbBBbB'),
    0x77: ('RemoveDarkScreen', ''),
    0x78: ('DisplayConditions', 'bB'),
    0x79: ('WalkToAnim', 'bbw'),
    0x7A: ('DismissUnit', 'bb'),
    0x7B: ('TutorialButtonWait', 'w'),
    0x7C: ('EndSound', ''),
    0x7D: ('ShowGraphic', 'b'),
    0x7E: ('WaitValue', 'ww'),
    0x7F: ('EVTCHRPalette', 'bbbb'),
    0x80: ('March', 'bbB'),
    0x83: ('ChangeStats', 'bbbs'),
    0x84: ('PlayTune', 'b'),
    0x85: ('UnlockDate', 'b'),
    0x86: ('TempWeapon', 'bbb'),
    0x87: ('Arrow', 'bbbb'),
    0x88: ('MapUnfreeze', ''),
    0x89: ('MapFreeze', ''),
    0x8A: ('EffectStart', ''),
    0x8B: ('EffectEnd', ''),
    0x8C: ('UnitAnimRotate', 'bbbwb'),
    0x8D: ('Inventory', 'bbb'),
    0x8E: ('WaitGraphicPrint', ''),
    0x90: ('TutorialCoordinateConfirm', 'BBb'),
    0x91: ('ShowMapTitle', 'ccc'),
    0x92: ('InflictStatus', 'bbbbb'),
    0x94: ('TeleportOut', 'bb'),
    0x96: ('AppendMapState', ''),
    0x97: ('ResetPalette', 'bb'),
    0x98: ('TeleportIn', 'bb'),
    0x99: ('BlueRemoveUnit', 'bb'),
    0x9A: ('ResetTextIncrement', ''),
    0x9B: ('PushTextVal', 'w'),
    0x9C: ('PushTextVar', 'b'),
    0x9D: ('SetTextVal', 'bw'),
    0x9E: ('SetTextVar', 'bb'),
    0x9F: ('ButtonPressGoTo', 'wb'),
    0xA0: ('LTE', ''),
    0xA1: ('GTE', ''),
    0xA2: ('EQ', ''),
    0xA3: ('NEQ', ''),
    0xA4: ('LT', ''),
    0xA5: ('GT', ''),
    0xAC: ('LUI', 'ww'),
    0xAD: ('LUIVar', 'ww'),
    0xAE: ('SET', 'ww'),
    0xAF: ('SETVar', 'ww'),
    0xB0: ('ADD', 'ww'),
    0xB1: ('ADDVar', 'ww'),
    0xB2: ('SUB', 'ww'),
    0xB3: ('SUBVar', 'ww'),
    0xB4: ('MULT', 'ww'),
    0xB5: ('MULTVar', 'ww'),
    0xB6: ('DIV', 'ww'),
    0xB7: ('DIVVar', 'ww'),
    0xB8: ('MOD', 'ww'),
    0xB9: ('MODVar', 'ww'),
    0xBA: ('AND', 'ww'),
    0xBB: ('ANDVar', 'ww'),
    0xBC: ('OR', 'ww'),
    0xBD: ('ORVar', 'ww'),
    0xBE: ('ZERO', 'w'),
    0xBF: ('Random', 'bW'),
    0xC1: ('LoadAddress', 'bwbB'),
    0xC2: ('UnitAddress', 'bb'),
    0xC3: ('WorldUnitAddress', 'bb'),
    0xC4: ('SaveAddress', 'bwbB'),
    0xC5: ('UpdateUnit', 'bb'),
    0xC6: ('TransformUnit', 'bbb'),
    0xC7: ('RosterHasUnit', 'bw'),
    0xCC: ('ResetTurnCounter', 'b'),
    0xCD: ('PostEvent', 'bw'),
    0xCE: ('LoadConditionals', 'w'),
    0xCF: ('Routine', 'd'),
    0xD0: ('JumpForwardIfZero', 'b'),
    0xD1: ('JumpForward', 'b'),
    0xD2: ('ForwardTarget', 'b'),
    0xD3: ('JumpBack', 'b'),
    0xD5: ('BackTarget', 'b'),
    0xDB: ('EventEnd', ''),
    0xE0: ('GenerateUnit', 'wbbbbbbBBbb'),
    0xE3: ('EventEnd2', ''),
    0xE5: ('WaitForInstruction', 'bb'),
    0xE6: ('AlterParameter', 'bb'),
    0xE7: ('GetTileUnit', 'ccb'),
    0xE8: ('LoadTileData', 'bbccb'),
    0xE9: ('SaveTileData', 'bbccb'),
    0xEA: ('LoadENTD', 'ww'),
    0xEB: ('LoadENTDUnit', 'bbbb'),
    0xEC: ('ConditionalDefeat', 'b'),
    0xED: ('LoadDatabase', 'BB'),
    0xEE: ('GetTableSize', 'bb'),
    0xEF: ('FetchTableRow', 'bb'),
    0xF1: ('Wait', 'W'),
    0xF2: ('Pad', ''),
}


# The remaster's layout of the two widened instructions: DisplayMessage is type, box style, line id, unit, two bytes,
# x, y, arrow, the closing byte, and a last byte (Ivalice ReMixed's centring; 0 otherwise); ChangeDialog is the box,
# the line id, two bytes. A line id of 0xFFFFFFFF is no line.
REMASTER_NAMES = dict(NAMES)
REMASTER_NAMES[0x10] = ("DisplayMessage", "bbLbbbsssbb")
REMASTER_NAMES[0x51] = ("ChangeDialog", "bLbb")

# The opcodes the upgrade handles before the remaster's interpreter (src/EventInstructionUpgrade/EventScript.cs,
# Ours), and of those, the ones it runs (Done); the rest it steps past with a note.
UPGRADE = frozenset([0x14, 0x15, 0x17, 0x2F, 0x8D, 0xBF, 0xE0, *range(0x00, 0x10), *range(0x9A, 0xA0),
                     *range(0xAC, 0xB0), *range(0xC1, 0xC8), *range(0xCC, 0xD0), *range(0xE6, 0xF0)])
RUNS = frozenset([*range(0x00, 0x10), 0x14, 0x8D, *range(0x9A, 0x9F), 0xAC, 0xAD, 0xAE, 0xAF, 0xBF,
                  0xC1, 0xC2, 0xC3, 0xC4, 0xC7, 0xCD, 0xCE, 0xCF, 0xE0, 0xE6, 0xE7, 0xE8, 0xE9, 0x2F])

WIDTHS = {"b": 1, "B": 1, "c": 1, "w": 2, "W": 2, "s": 2, "d": 4, "L": 4}


def name(op: int) -> str:
    """An opcode's name, or Op followed by its hex for one with none (Op23)."""
    return NAMES[op][0] if op in NAMES else f"Op{op:02X}"


def kinds(op: int, lengths: bytes, remaster: bool) -> str:
    """An instruction's parameter kinds; an opcode with no known layout (or one that doesn't fit the length table)
    gets a hex byte per parameter byte."""
    table = REMASTER_NAMES if remaster else NAMES
    if op in table:
        spec = table[op][1]
        if sum(WIDTHS[k] for k in spec) == lengths[op]:
            return spec
    return "b" * lengths[op]


BY_NAME = {n.lower(): op for op, (n, _) in NAMES.items()}
