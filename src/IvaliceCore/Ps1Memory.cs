namespace IvaliceCore;

/// <summary>
/// The PS1 addresses events read and write (the Event Instruction Upgrade's LoadAddress and SaveAddress, as ReMixed's
/// events use them), in the remaster's memory: the battle units (0x801908CC, 21 ×
/// 0x1C0; the remaster's 21 × 0x200) field by field, and the roster (0x80057F74, 20 × 0x100; the remaster's slots
/// of 0x258), and single globals given to <see cref="Map"/>. An address with no known place is kept in a store of the
/// mod's own (so ReMixed's own data reads back what it wrote, as on the PS1; it starts as 0), noted once.
/// </summary>
internal sealed unsafe class Ps1Memory(byte* units, byte* roster, Action<string> note)
{
    private readonly Dictionary<uint, nint> _globals = [];
    private readonly Dictionary<uint, byte> _own = [];

    private readonly List<(uint Start, uint End, Action Refresh)> _refreshed = [];

    /// <summary>PS1 <paramref name="address"/> and the <paramref name="length"/> bytes after it live at <paramref name="place"/>.</summary>
    public Ps1Memory Map(uint address, int length, byte* place)
    {
        for (var k = 0; k < length; k++)
            _globals[address + (uint)k] = (nint)(place + k);
        return this;
    }

    /// <summary>Before a read of PS1 <paramref name="address"/> or the <paramref name="length"/> bytes after it,
    /// <paramref name="refresh"/> makes them (memory the mod makes as it's read: <see cref="Ps1Pad"/>'s buttons).</summary>
    public Ps1Memory Refreshed(uint address, int length, Action refresh)
    {
        _refreshed.Add((address, address + (uint)length, refresh));
        return this;
    }

    internal const uint Units = 0x801908CC, UnitSize = 0x1C0, Roster = 0x80057F74, RosterSize = 0x100;
    /// <summary>The per-event map settings, a u16 per event number (PS1 0x8004E5D0, 0x6EE bytes; the remaster's
    /// 0xD3F950, which its world setup fills from its scenario records at 0x2FBB15 and saves with the game).</summary>
    internal const uint MapSettings = 0x8004E5D0;
    internal const int MapSettingsBytes = 0x6EE;
    /// <summary>The buttons, in the PS1's layout (controller 1 in the low half: Select 0x100, Start 0x800, Cross 0x40;
    /// L1 + R1 + Start + Select, 0x90C, resets): held (PS1 0x80045944, the remaster's 0x1876EEC), held the frame before
    /// (0x80045948, 0x187A3F4) and newly pressed (0x8004594C, 0x1876EF0), which the remaster's battle-mode loop makes
    /// every frame (0x37FB7C, the PS1's BATTLE 0x80093654). 165 of ReMixed's events test Start this way.</summary>
    /// <summary>The event variables as memory: 0x80 words, then the flags 0x80–0x35F a bit each (+0x200), then
    /// 0x360–0x3FF four bits each (+0x25C), the PS1's 0x8005771C laid out as the remaster's ([0x7830D8], 0xD40060;
    /// its address helper 0x242984 keeps the PS1's layout).</summary>
    internal const uint Variables = 0x8005771C;
    internal const int VariablesBytes = 0x2AC;

    /// <summary>The skillset types, a byte per skillset (SCUS 0x80065CB4; the remaster's .rdata 0x67E010, found by
    /// <see cref="SkillsetTypes.Find"/>). ReMixed's location battles read them for each enemy's skillsets.</summary>
    internal const uint SkillsetTypeTable = 0x80065CB4;
    internal const int SkillsetTypeBytes = 0x100;

    internal const uint Buttons = 0x80045944, ButtonsBefore = 0x80045948, ButtonsPressed = 0x8004594C;

    /// <summary>The battle event conditions: 16 offsets (PS1 0x800475E0, the remaster's 0xD40040) into the records of
    /// five halfwords and more (0x80049A18, the remaster's 0xD3F550), which the check (0x801425B0, the remaster's 0x2391C4,
    /// both with 16 sets) runs each turn.</summary>
    internal const uint ConditionSets = 0x800475E0, Conditions = 0x80049A18;
    internal const int ConditionSetsBytes = 0x20, ConditionsBytes = 0x200;
    /// <summary>The Event Instruction Upgrade's words (its variables 0xB00–0xB7F, its text values) and the text values'
    /// index (BATTLE.BIN memory).</summary>
    internal const uint ExtraWords = 0x80165EF4, ListIndex = 0x8015029A;
    internal const int ExtraWordsBytes = 0x400;
    internal const int UnitCount = 21, RosterSlots = 20;
    private readonly HashSet<uint> _missing = [];

    /// <summary>A roster unit's name in the PS1's layout (+0xBE, 16 bytes in the game's character set, 0xFE ending it);
    /// the remaster keeps it as UTF-8 at +0xDC (0x40 bytes). A write there (ReMixed's Algus campaign names the real
    /// Ramza again, 0x0FD) is kept in the mod's store and the name written over the remaster's.</summary>
    internal const int NameAt = 0xBE, NameLength = 16, RemasterNameAt = 0xDC, RemasterNameBytes = 0x40;
    /// <summary>A roster unit's JP by job (PS1 +0x6E, 20 halfwords, the totals from +0x96) and the remaster's (+0x80,
    /// +0xAE: <see cref="RosterField"/>). A JP written there stops at 9999, as the remaster's own JP add does (0x283664);
    /// ReMixed's Treasure Wheel adds its cash-in with no cap.</summary>
    internal const int JpAt = 0x6E, TotalJpAt = 0x96, PsJobs = 20, RemasterJpAt = 0x80, RemasterTotalJpAt = 0xAE,
                       MostJp = 9999;
    /// <summary>A battle unit's JP by job and its totals (PS1 +0xDC and +0x104, 20 halfwords each, copied from the
    /// roster's by the PS1's unit build, SCUS 0x8005B1EC) and the remaster's (+0xF0 and +0x11E, 23 each, from its roster's
    /// by 0x276758). Squire to Mime are 0-19 in both; a JP written there stops at 9999 too.</summary>
    internal const int UnitJpAt = 0xDC, UnitTotalJpAt = 0x104, RemasterUnitJpAt = 0xF0, RemasterUnitTotalJpAt = 0x11E;
    /// <summary>A battle unit's name in the PS1's layout (+0x12C, 16 bytes, as a roster unit's); the remaster keeps it as
    /// UTF-8 at +0x14C (0x40 bytes, from the roster's by its unit build, 0x276758). A write there (Be A Beta names its
    /// beta testers so, 0x0C6) is kept in the mod's store and the name written over the remaster's, which it shows for
    /// a unit whose name row is blank (<see cref="NameGuard"/>) or generic; a story character keeps the remaster's.</summary>
    internal const int UnitNameAt = 0x12C, RemasterUnitNameAt = 0x14C;

    /// <summary>A character of a PS1 name (digits, A-Z and a-z from 0, 0x0A and 0x24; 0xFA a space).</summary>
    internal static char NameChar(int b) => b switch
    {
        < 10 => (char)('0' + b),
        < 0x24 => (char)('A' + b - 10),
        < 0x3E => (char)('a' + b - 0x24),
        0x3E => '!',
        0x40 => '?',
        0x5F => '.',
        0x8D => '-',
        0x93 => (char)0x27,
        0xFA => ' ',
        _ => '?',
    };

    /// <summary>A PS1 name's text from its bytes (to 0xFE or 0xFF).</summary>
    internal static string Name(Func<int, int> at)
    {
        var text = new System.Text.StringBuilder();
        for (var k = 0; k < NameLength; k++)
        {
            var b = at(k);
            if (b >= 0xFE)
                break;
            text.Append(NameChar(b));
        }
        return text.ToString();
    }

    /// <summary>The name once it's whole: ReMixed writes one a word at a time, its end (0xFE or 0xFF) in the last.</summary>
    private void WriteUnitName(uint slot)
    {
        var start = Units + slot * UnitSize + UnitNameAt;
        if (!Enumerable.Range(0, NameLength).Any(k => _own.GetValueOrDefault(start + (uint)k) >= 0xFE))
            return;
        var name = Name(k => _own.GetValueOrDefault(start + (uint)k));
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        var target = new Span<byte>(units + slot * Battle.UnitSize + RemasterUnitNameAt, RemasterNameBytes);
        target.Clear();
        bytes.AsSpan(0, Math.Min(bytes.Length, RemasterNameBytes - 1)).CopyTo(target);
        note($"battle unit {slot}'s name written: \"{name}\"");
    }

    private void WriteName(uint slot)
    {
        var start = Roster + slot * RosterSize + NameAt;
        var name = Name(k => _own.GetValueOrDefault(start + (uint)k));
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        var target = new Span<byte>(roster + slot * UnitTables.RosterSize + RemasterNameAt, RemasterNameBytes);
        target.Clear();
        bytes.AsSpan(0, Math.Min(bytes.Length, RemasterNameBytes - 1)).CopyTo(target);
        note($"roster slot {slot}'s name written: \"{name}\"");
    }

    /// <summary>A PS1 battle unit's byte offset in the remaster's unit, or −1 (docs/MEMORY-MAP.md).</summary>
    internal static int UnitField(int offset) => offset switch
    {
        < 0x1A => offset,                                  // ids, job, team (+5), gender (+6), skillsets
        >= 0x1A and <= 0x20 => 0x1A + 2 * (offset - 0x1A), // equipment: a byte each, the remaster's halfwords' low bytes
        >= 0x21 and <= 0x26 => offset + 7,                // EXP, level, Brave, Faith
        >= 0x28 and <= 0x4D => offset + 8,                // HP, MP, stats, CT, Move, Jump, X, Y, level, equip types
        >= 0x4E and <= 0x71 => offset + 9,                // innate statuses, immunities, statuses, their counters,
                                                           // elements (the remaster's 5th equip type byte is +0x56)
        >= 0x72 and <= 0x80 => offset + 9,                // raw HP, MP, Speed, PA, MA (3 bytes each)
        >= 0x8B and <= 0x95 => offset + 9,                // reactions, supports, movement
        >= 0x96 and <= 0xD1 => offset + 9,                // unlocked jobs; learned abilities (3 bytes for each of the
                                                           // PS1's 19 jobs with abilities; the remaster has 22)
        >= 0xD2 and <= 0xDB => offset + 0x12,             // job levels, a nibble a job (the even index high)
        >= UnitJpAt and < UnitJpAt + 2 * PsJobs => offset - UnitJpAt + RemasterUnitJpAt,                 // JP by job
        >= UnitTotalJpAt and < UnitTotalJpAt + 2 * PsJobs => offset - UnitTotalJpAt + RemasterUnitTotalJpAt, // total JP
        >= 0x15C and <= 0x162 => offset + 0x30,            // +0x15E–0x160 the job's graphic, portrait, palette;
                                                           // +0x161 the battle data's unit id; +0x162 the job's skillset
        0x163 => 0x194,                                    // widened to a halfword in the remaster (the setup clears it
        0x164 => 0x196,                                    // with +0x164, 0x8005DFF8 / 0x278E0C): from here on +0x32
        0x16C or 0x16D => offset - 0x16C + 0x1FC,          // the name id
        >= 0x182 and <= 0x18C => offset + 0x32,
        >= 0x1B8 and <= 0x1BF => offset + 0x34,            // +0x1B8, +0x1B9 cleared with the turn's flags; team,
                                                           // the second status copy
        _ => -1,
    };

    /// <summary>A PS1 roster byte offset in the remaster's record, or −1 (docs/MEMORY-MAP.md).</summary>
    /// <remarks>From the remaster's unit build from the roster (0x276758) beside the PS1's (SCUS 0x8005B1xx: the level
    /// is roster +0x16): the equipment is halfwords (+0x0E–0x1B), so from EXP on it's +7, through the raw stats, the
    /// unlocked jobs and the learned abilities of the PS1's 20 jobs (to +0x66). The remaster has 22 jobs, so the job
    /// levels after them are laid out otherwise and aren't mapped; its JP are 23 halfwords from +0x80 and the totals 23
    /// from +0xAE (which the unit build copies to the battle unit's +0xF0 and +0x11E), by its job index (0x2B8F18):
    /// Squire to Mime are 0–19 in both, so the PS1's 20 (+0x6E, the totals +0x96; ReMixed's Treasure Wheel adds its JP
    /// cash-in there) are the first 20 of the remaster's.</remarks>
    internal static int RosterField(int offset) => offset switch
    {
        < 0x0E => offset,
        >= 0x0E and <= 0x14 => 0x0E + 2 * (offset - 0x0E), // equipment: the low bytes of the remaster's halfwords
        >= 0x15 and <= 0x63 => offset + 7,                // EXP, level, Brave, Faith, raw stats, unlocked jobs,
                                                           // learned abilities (19 sets of 3 bytes from +0x2B)
        >= 0x64 and <= 0x6D => offset + 0x10,             // job levels, a nibble a job, the even index high (the
                                                           // remaster's 12 bytes from +0x74, after its 22 sets)
        >= JpAt and < JpAt + 2 * PsJobs => offset - JpAt + RemasterJpAt,                  // JP by job
        >= TotalJpAt and < TotalJpAt + 2 * PsJobs => offset - TotalJpAt + RemasterTotalJpAt, // total JP by job
        0xCE or 0xCF => offset - 0xCE + 0x230,             // the name id (to be confirmed)
        0xD0 => 0x11E,                                     // the errand byte
        _ => -1,
    };

    /// <summary>
    /// Unit fields an event may read but not change in the remaster. +0x1BA, the base team: random battles' event
    /// (0x191) clears its enemy bits (0x30) on every unit it counts, which the PS1 survives but which in the remaster
    /// (+0x1EE, the team it plays by) leaves no enemies, so the battle is won at once (seen on the Deck, 2026-11-04).
    /// Writes to them go to the mod's own store, as do writes to the skillset types (the remaster's are in .rdata).
    /// </summary>
    internal static bool ReadOnly(uint address) =>
        address - Units < UnitCount * UnitSize && (address - Units) % UnitSize == 0x1BA
        || address - SkillsetTypeTable < SkillsetTypeBytes;

    private byte* Place(uint address)
    {
        if (_globals.TryGetValue(address, out var global))
            return (byte*)global;
        if (address - Units < UnitCount * UnitSize)
        {
            var field = UnitField((int)((address - Units) % UnitSize));
            return field < 0 ? null : units + (address - Units) / UnitSize * Battle.UnitSize + field;
        }
        if (address - Roster < RosterSlots * RosterSize)
        {
            var field = RosterField((int)((address - Roster) % RosterSize));
            return field < 0 ? null : roster + (address - Roster) / RosterSize * UnitTables.RosterSize + field;
        }
        return null;
    }

    private string Describe(uint address) =>
        address - Units < UnitCount * UnitSize ? $"battle unit {(address - Units) / UnitSize} +0x{(address - Units) % UnitSize:X}"
        : address - Roster < RosterSlots * RosterSize ? $"roster slot {(address - Roster) / RosterSize} +0x{(address - Roster) % RosterSize:X}"
        : "not mapped";

    public int Read(uint address, int length)
    {
        foreach (var (start, end, refresh) in _refreshed)
            if (address < end && address + (uint)length > start)
                refresh();
        var value = 0;
        for (var k = length - 1; k >= 0; k--)
        {
            var at = address + (uint)k;
            var place = Place(at);
            if (place == null)
                Missing(at, "kept in the mod's own store");
            value = value << 8 | (place == null ? _own.GetValueOrDefault(at) : *place);
        }
        return value;
    }

    public void Write(uint address, int length, int value)
    {
        if (length == 2 && IsJp(address))
            value = Math.Min(value, MostJp);
        for (var k = 0; k < length; k++)
        {
            var at = address + (uint)k;
            var place = ReadOnly(at) ? null : Place(at);
            if (place == null)
            {
                if (!IsName(at) && !IsUnitName(at))
                    Missing(at, "kept in the mod's own store");
                _own[at] = (byte)(value >> (8 * k));
            }
            else
                *place = (byte)(value >> (8 * k));
        }
        if (IsUnitName(address) || IsUnitName(address + (uint)length - 1))
            WriteUnitName((address - Units) / UnitSize);
        if (IsName(address) || IsName(address + (uint)length - 1))
            WriteName((address - Roster) / RosterSize);
        if (GraphicWritten != null && address - Units < UnitCount * UnitSize
            && UnitGraphicAt - ((address - Units) % UnitSize) < (uint)length)
            GraphicWritten((address - Units) / UnitSize);
    }

    /// <summary>A battle unit's graphic (PS1 +0x15F) was written: the unit keeps that look (<see cref="UnitLooks"/>).</summary>
    public Action<uint>? GraphicWritten { get; set; }
    internal const uint UnitGraphicAt = 0x15F;

    private static bool IsUnitName(uint address) =>
        address - Units < UnitCount * UnitSize && (address - Units) % UnitSize - (uint)UnitNameAt < (uint)NameLength;

    private static bool IsName(uint address) =>
        address - Roster < RosterSlots * RosterSize && (address - Roster) % RosterSize - (uint)NameAt < (uint)NameLength;

    /// <summary>A roster unit's or a battle unit's JP or total JP (<see cref="JpAt"/>, <see cref="UnitJpAt"/>).</summary>
    private static bool IsJp(uint address) =>
        address - Roster < RosterSlots * RosterSize && (address - Roster) % RosterSize - (uint)JpAt < 4u * PsJobs
        || address - Units < UnitCount * UnitSize && (address - Units) % UnitSize - (uint)UnitJpAt < 4u * PsJobs;

    /// <summary>Noted once per unit field, roster field or other address.</summary>
    private void Missing(uint address, string what)
    {
        var key = address - Units < UnitCount * UnitSize ? 0x10000 | (address - Units) % UnitSize
            : address - Roster < RosterSlots * RosterSize ? 0x20000 | (address - Roster) % RosterSize
            : address & ~0xFu;
        if (_missing.Add(key))
            note($"PS1 0x{address:X8} ({Describe(address)}) has no place in the remaster yet; {what}");
    }

    private byte* Unit(int k) => units + k * Battle.UnitSize;

    /// <summary>0xC2: the unit whose battle data id is <paramref name="id"/>, else whose sprite set is; its PS1 address or 0.</summary>
    public uint FindUnit(int id)
    {
        for (var k = 0; k < UnitCount; k++)
            if (Unit(k)[0x191] == id)
                return Units + (uint)k * UnitSize;
        for (var k = 0; k < UnitCount; k++)
            if (Unit(k)[0] == id)
                return Units + (uint)k * UnitSize;
        return 0;
    }

    /// <summary>0xC3: the roster slot (of the PS1's 20) with that sprite set or name id; its PS1 address or 0.</summary>
    public uint FindRoster(int id)
    {
        for (var k = 0; k < RosterSlots; k++)
        {
            var slot = roster + k * UnitTables.RosterSize;
            if (slot[1] != 0xFF && (slot[0] == id || *(ushort*)(slot + 0x230) == id))
                return Roster + (uint)k * RosterSize;
        }
        return 0;
    }

    /// <summary>0xC7: whether the roster (<paramref name="flags"/> 0x80) has the character: by name id with bit 0, else
    /// by sprite set. The PS1's second list (flag 0x40, 0x800579C8) isn't mapped.</summary>
    public bool HasCharacter(int flags, int id)
    {
        if ((flags & 0x80) != 0)
        {
            for (var k = 0; k < RosterSlots; k++)
            {
                var slot = roster + k * UnitTables.RosterSize;
                if (slot[1] != 0xFF && ((flags & 1) != 0 ? *(ushort*)(slot + 0x230) : slot[0]) == id)
                    return true;
            }
        }
        if ((flags & 0x40) != 0 && _missing.Add(0x800579C8u))
            note("HAS_CHARACTER's second list (PS1 0x800579C8) has no place in the remaster yet");
        return false;
    }

    /// <summary>0xE7: the unit on that tile (x, y, upper level); its PS1 address or 0.</summary>
    public uint UnitOnTile(int x, int y, int level)
    {
        for (var k = 0; k < UnitCount; k++)
        {
            var unit = Unit(k);
            if (unit[1] != 0xFF && unit[Battle.UnitX] == x && unit[Battle.UnitY] == y && unit[Battle.UnitLevel] >> 7 == level)
                return Units + (uint)k * UnitSize;
        }
        return 0;
    }
}
