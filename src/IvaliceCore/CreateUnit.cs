using System.Runtime.InteropServices;

namespace IvaliceCore;

/// <summary>
/// ReMixed's 0xE0, "create a unit" (BATTLE.BIN 0x80154800, where vanilla's 0xE0 did nothing; its parameters in
/// docs/INSTRUCTIONS.md, GenerateUnit): a battle unit made in slot p3 from a 40-byte battle-data record (the ENTD
/// layout), which ReMixed builds from the instruction's parameters (flag 0x40), from a roster slot (0x02, or the slot
/// found by sprite set or unit id) or from another battle unit, then fixes up. Its events then show the unit with
/// vanilla's add-unit instructions (0x49, 0x45, 0x4A, 0x4B). The remaster makes it as its battle setup (0x27128C)
/// makes each of a battle's units: the record in the setup's current-record global, 0x275CA4(unit, record, 0, 0, 0),
/// then 0x278EDC (the unit's graphic, into +0x18F and +0x190) and 0x278E0C(unit, 0): the PS1's 0x8005A9B4,
/// 0x8005E088 and 0x8005DFD4. <see cref="Build"/> is ReMixed's record building read through <see cref="Ps1Memory"/>.
/// </summary>
internal static unsafe class CreateUnit
{
    /// <summary>The battle setup's loop (0x2713CA): mov [current record], rsi ... call 0x275CA4.</summary>
    internal const string MakePattern =
        "48 89 35 ?? ?? ?? ?? 44 38 3E 75 10 44 88 73 01 44 88 B3 B5 01 00 00 E9 ?? ?? ?? ?? B9 01 00 00 00 44 88 63 01 " +
        "88 8B B5 01 00 00 45 33 C9 48 8B CB 48 89 44 24 20 45 33 C0 48 8B D6 E8";
    /// <summary>The same loop on (0x271459): call 0x278EDC(unit, &amp;out, unit[3]), +0x190 = out, +0x18F = its byte,
    /// call 0x278E0C(unit, 0).</summary>
    internal const string StepsPattern =
        "44 8A 43 03 48 8D 94 24 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 44 0F B6 F0 33 D2 8A 84 24 ?? ?? ?? ?? 48 8B CB " +
        "88 83 90 01 00 00 44 88 B3 8F 01 00 00 E8";
    private const int MakeCall = 60, GraphicCall = 15, FinishCall = 49;

    /// <summary>Told the record's +0x25 (ReMixed's unit preset) before a unit is made from it, and -1 after (ReMixed's
    /// UnitPresets keeps it for the unit); the byte is cleared for the remaster's own unit making.</summary>
    internal static Action<int>? PresetChosen;

    internal const int RecordSize = 40, Parameters = 12;
    /// <summary>The remaster's unit fields the steps use (PS1 in brackets): the graphic and its palette (+0x15F,
    /// +0x160), the battle data's unit id (+0x161), +0x183, the name (+0x12C, 15 bytes; the remaster's 0x40 at +0x14C,
    /// from roster +0xDC by its build from the roster, 0x276758) and the slot (the setup's +0x1BC).</summary>
    private const int UnitGraphic = 0x18F, UnitGraphicPalette = 0x190, UnitId = 0x191, Unit183 = 0x1B5, UnitName = 0x14C,
                      NameBytes = 0x40, UnitSlot = 0x1BC, RosterName = 0xDC;

    internal static nint MakeAt, GraphicAt, FinishAt, CurrentAt;
    private static delegate* unmanaged<byte*, byte*, int, int, nint, long> _make;
    private static delegate* unmanaged<byte*, byte*, byte, byte> _graphic;
    private static delegate* unmanaged<byte*, int, int> _finish;
    private static byte** _current;
    /// <summary>ReMixed's record (0x8015F880), kept between uses: flag 0x80 makes a unit from it as it is.</summary>
    private static readonly byte* Record = (byte*)NativeMemory.AllocZeroed(RecordSize);

    internal static void Find(GameImage image)
    {
        var make = image.FindUnique(MakePattern);
        var steps = image.FindUnique(StepsPattern);
        CurrentAt = GameImage.Rip(make, 3, 7);
        MakeAt = GameImage.Rip(make + MakeCall, 1, 5);
        GraphicAt = GameImage.Rip(steps + GraphicCall, 1, 5);
        FinishAt = GameImage.Rip(steps + FinishCall, 1, 5);
        _current = (byte**)CurrentAt;
        _make = (delegate* unmanaged<byte*, byte*, int, int, nint, long>)MakeAt;
        _graphic = (delegate* unmanaged<byte*, byte*, byte, byte>)GraphicAt;
        _finish = (delegate* unmanaged<byte*, int, int>)FinishAt;
    }

    internal static bool Found => _make != null;

    /// <summary>
    /// ReMixed's record building (0x80154810-0x80154CF8), with PS1 memory read through <paramref name="read"/> (a
    /// byte): the record in <paramref name="record"/> (left as it was by flag 0x80), whether a unit is made (a roster
    /// search that finds nothing makes none), and the PS1 address of the roster slot (or unit) the fix-ups read.
    /// </summary>
    internal static (bool Make, uint Source) Build(ReadOnlySpan<byte> p, Span<byte> record, Func<uint, int> read)
    {
        int s3 = p[0] | p[1] << 8, flags = p[11];
        var slot = p[3] >= Ps1Memory.UnitCount ? 0 : p[3];
        var unit = Ps1Memory.Units + (uint)slot * Ps1Memory.UnitSize;
        var source = Ps1Memory.Roster;
        if ((flags & 0x80) != 0)
            return (true, source);
        record.Slice(0x1E, 10).Clear();
        record[8] = p[4];
        record[9] = p[5];
        record[3] = p[6] != 0 ? p[6] : (byte)read(unit + 0x22);
        record[0x25] = p[7];
        if ((flags & 0x40) != 0)
        {
            FromParameters(record, s3);
            return (true, source);
        }
        var left = 0x13; // the search's other slots
        if ((flags & 2) != 0)
        {
            if ((s3 & 0x300) != 0)
            {
                if ((s3 & 0x100) == 0)
                    return (true, source); // 0x200: ReMixed's code goes on only for s3 < 16, which can't be; the record as built
                source = Ps1Memory.Units + (uint)(s3 & 0xFF) * Ps1Memory.UnitSize;
                FromUnit(record, source, p[6], read);
            }
            else
                source += (uint)(s3 < 20 ? s3 : 0) * Ps1Memory.RosterSize;
            left = 0;
            s3 = read(source);
            flags &= 0xFE;
        }
        while (read(source + 1) == 0xFF || ((flags & 1) != 0 ? read(source + 0xCE) : read(source)) != s3)
        {
            if (left-- <= 0)
                return (false, 0);
            source += Ps1Memory.RosterSize;
        }
        FromRoster(record, source, p[6], flags, read);
        return (true, source);
    }

    private static void Half(Span<byte> record, int at, int value)
    {
        record[at] = (byte)value;
        record[at + 1] = (byte)(value >> 8);
    }

    /// <summary>Flag 0x40 (0x801548DC): p0 the job, and for specials (under 0x4A) the sprite set and name too; a
    /// generic's sprite set by p1's 0x8000 (male) and 0xC000 (monster) bits; level 100 (the party's); birthday, Brave
    /// and Faith, abilities and equipment random (0xFE); the team bits from p1.</summary>
    private static void FromParameters(Span<byte> record, int s3)
    {
        var job = s3 & 0xFF;
        record[0xA] = (byte)job;
        record[2] = (byte)job;
        var sprite = job;
        if (job >= 0x4A)
        {
            record[2] = 0xFF;
            sprite = (s3 & 0xC000) == 0xC000 ? 0x82 : (s3 & 0x8000) != 0 ? 0x80 : 0x81;
        }
        record[0] = (byte)sprite;
        var high = s3 >> 8;
        var gender = (high & 0xC0) == 0xC0 ? 0x20 : high & 0xC0;
        var kind = gender | high & 0x14;
        record[1] = (byte)kind;
        record[3] = 100;
        record.Slice(4, 4).Fill(0xFE);
        if ((kind & 0x20) != 0)
        {
            record[8] = record[9] = 0;
            record[0xB] = 0;
            record.Slice(0xC, 12).Clear();
        }
        else
        {
            record[0xB] = 0xFE;
            Half(record, 0xC, 0x1FE);
            Half(record, 0xE, 0x1FE);
            Half(record, 0x10, 0x1FE);
            Half(record, 0x12, 0xFEFE);
            Half(record, 0x14, 0xFEFE);
            record[0x16] = 0xFE;
            record[0x17] = 0; // ReMixed's palette test (sprite set + 128 < 2) is never true
        }
        record[0x18] = (byte)(high & 8 | (high & 3) << 4 | (high & 0x20) >> 3);
        record[0x1C] = 0xFE;
        record[0x1D] = 0xFF;
    }

    /// <summary>From roster slot <paramref name="s2"/> (0x80154BB0; the PS1's fields, which <see cref="Ps1Memory"/>
    /// maps to the remaster's).</summary>
    private static void FromRoster(Span<byte> record, uint s2, int level, int flags, Func<uint, int> read)
    {
        var sprite = read(s2);
        var gender = read(s2 + 4);
        record[0] = (byte)(sprite < 0x90 ? sprite : (gender & 0x80) != 0 ? 0x80 : (gender & 0x20) != 0 ? 0x82 : 0x81);
        record[1] = (byte)(gender & 0xE0 | ((flags & 0x10) != 0 ? 9 : 0));
        record[3] = (byte)(level != 0 ? level : read(s2 + 0x16));
        var id = read(s2 + 0xCE) | read(s2 + 0xCF) << 8;
        record[2] = (byte)(id < 0x100 ? id : 0xFF);
        record[4] = (byte)read(s2 + 5);
        record[5] = (byte)(read(s2 + 6) & 0xF);
        record[6] = (byte)read(s2 + 0x17);
        record[7] = (byte)read(s2 + 0x18);
        record[0xA] = (byte)read(s2 + 2);
        record[0xB] = (byte)read(s2 + 7);
        for (var k = 0; k < 6; k++)
            record[0xC + k] = (byte)read(s2 + 8 + (uint)k);
        record[0x12] = (byte)read(s2 + 0xE);
        record[0x13] = (byte)read(s2 + 0xF);
        record[0x14] = (byte)read(s2 + 0x10);
        var right = read(s2 + 0x11);
        record[0x15] = (byte)(right != 0xFF ? right : read(s2 + 0x12));
        var leftHand = read(s2 + 0x13);
        record[0x16] = (byte)(leftHand != 0xFF ? leftHand : read(s2 + 0x14));
        record[0x17] = 0;
        record[0x18] = (byte)(read(s2 + 1) != 0 && (read(s2 + 0xD0) & 0x10) == 0 && sprite >= 4 ? 8 : 0xE);
        record[0x1C] = (byte)read(s2 + 0x15);
        record[0x1D] = 0xFF;
    }

    /// <summary>From battle unit <paramref name="u"/> (0x80154A44); ReMixed then goes on to the roster's steps over
    /// the same unit (as <see cref="Build"/> does).</summary>
    private static void FromUnit(Span<byte> record, uint u, int level, Func<uint, int> read)
    {
        record[0] = (byte)read(u);
        record[1] = (byte)read(u + 6);
        record[3] = (byte)(level != 0 ? level : read(u + 0x22));
        record[2] = (byte)read(u + 0x16C);
        record[4] = (byte)read(u + 8);
        record[5] = (byte)read(u + 9);
        record[6] = (byte)read(u + 0x23);
        record[7] = (byte)read(u + 0x25);
        record[0xA] = (byte)read(u + 3);
        record[0xB] = (byte)read(u + 0x13);
        for (var k = 0; k < 6; k++)
            record[0xC + k] = (byte)read(u + 0x14 + (uint)k);
        record[0x12] = (byte)read(u + 0x1A);
        record[0x13] = (byte)read(u + 0x1B);
        record[0x14] = (byte)read(u + 0x1C);
        var right = read(u + 0x1D);
        record[0x15] = (byte)(right != 0xFF ? right : read(u + 0x1E));
        var leftHand = read(u + 0x1F);
        record[0x16] = (byte)(leftHand != 0xFF ? leftHand : read(u + 0x20));
        record[0x17] = (byte)read(u + 4);
        record[0x18] = (byte)read(u + 0x1BA);
        record[0x1C] = (byte)read(u + 0x21);
        record[0x1D] = (byte)read(u + 0x12);
        record[0x1E] = (byte)read(u + 0x163);
        record[0x1F] = (byte)read(u + 0x164);
        var kept = record[0x25];
        for (var k = 0; k < 8; k++)
            record[0x20 + k] = (byte)read(u + 0x164 + (uint)k);
        record[0x20] = (byte)read(u + 0x161);
        record[0x25] = kept;
    }

    /// <summary>The job graphic ReMixed gives a unit from the roster whose sprite set is 0x90 or more (0x80154DC0):
    /// four of its new characters by name, the rest by sprite set and gender.</summary>
    internal static int Graphic(int sprite, int gender) => sprite switch
    {
        0xB2 => 0x82,
        0xB3 => 0x37,
        0xB4 => 0x38,
        0xB5 => 0x83,
        _ => (sprite > 0xB5 ? sprite - 2 : sprite) - 48 + ((gender & 0x80) != 0 ? 0 : 1),
    };

    /// <summary>
    /// 0xE0 run: the record built (<see cref="Build"/>), the unit made in its slot by the remaster's three calls (var
    /// 0x70, ReMixed's level-scaling word, at 0 meanwhile, as ReMixed does), then ReMixed's fix-ups: the unit id
    /// (p2), +0x183 cleared, its place (p8-p10, unless p11 &amp; 4), +0x37 kept (p11 &amp; 8), +2 and +6 (p11 &amp;
    /// 0x20), and for a roster unit +9, its name id and graphic; the name the slot had, or the roster unit's.
    /// </summary>
    internal static string Make(byte* p, Ps1Memory memory, byte* units, byte* roster, Func<int, int> get, Action<int, int> set)
    {
        if (!Found)
            return "0xE0: the remaster's unit making wasn't found; passed over";
        var parameters = new ReadOnlySpan<byte>(p, Parameters);
        var slot = p[3] >= Ps1Memory.UnitCount ? 0 : p[3];
        var unit = units + slot * Battle.UnitSize;
        var ps1Unit = Ps1Memory.Units + (uint)slot * Ps1Memory.UnitSize;
        var (make, source) = Build(parameters, new Span<byte>(Record, RecordSize), a => memory.Read(a, 1));
        if (!make)
            return $"0xE0 ({Convert.ToHexString(parameters)}): no roster unit matches; nothing made";
        if ((Record[1] & 8) != 0 && (p[11] & 0x80) == 0)
            return $"0xE0 ({Convert.ToHexString(parameters)}): its record loads a formation (+1 & 8), which the remaster's "
                   + "unit making skips; nothing made";
        var name = new byte[NameBytes];
        new ReadOnlySpan<byte>(unit + UnitName, NameBytes).CopyTo(name);
        var (x, y, level) = (unit[Battle.UnitX], unit[Battle.UnitY], unit[Battle.UnitLevel]);
        if ((p[11] & 0x84) == 0)
            (x, y, level) = (p[8], p[9], p[10]);
        var savedMa = unit[Battle.UnitMa];
        var scaling = get(0x70);
        set(0x70, 0);
        unit[1] = (byte)slot;
        unit[UnitSlot] = (byte)slot;
        *_current = Record;
        // The record's +0x25 (p7: ReMixed's unit preset) goes to PresetChosen for the unit made next: the remaster's
        // battle data has another use for the byte (a nonzero one crashed it).
        var preset = Record[0x25];
        PresetChosen?.Invoke(preset);
        Record[0x25] = 0;
        var made = _make(unit, Record, 0, 0, 0);
        Record[0x25] = preset; // kept, as ReMixed's record is (flag 0x80 makes a unit from it again)
        PresetChosen?.Invoke(-1);
        unit[Unit183] = 0;
        unit[UnitId] = p[2];
        byte palette = 0;
        unit[UnitGraphic] = _graphic(unit, &palette, unit[3]);
        unit[UnitGraphicPalette] = palette;
        _finish(unit, 0);
        set(0x70, scaling);
        unit[Battle.UnitX] = x;
        unit[Battle.UnitY] = y;
        unit[Battle.UnitLevel] = (byte)(unit[Battle.UnitLevel] & 0x70 | level & 0x8F);
        if ((p[11] & 8) != 0)
            unit[Battle.UnitMa] = savedMa;
        if ((p[11] & 0x20) != 0)
        {
            unit[2] = 0xFF;
            unit[6] &= 0xF6;
        }
        var from = "its parameters";
        if ((p[11] & 0xC0) == 0)
        {
            unit[9] = (byte)(unit[9] & 0xF | memory.Read(source + 6, 1) & 0xF0);
            memory.Write(ps1Unit + 0x16C, 2, memory.Read(source + 0xCE, 2));
            var sprite = memory.Read(source, 1);
            if (sprite >= 0x90)
                unit[UnitGraphic] = (byte)Graphic(sprite, memory.Read(source + 4, 1));
            if (source - Ps1Memory.Roster < Ps1Memory.RosterSlots * Ps1Memory.RosterSize)
            {
                var rosterSlot = (source - Ps1Memory.Roster) / Ps1Memory.RosterSize;
                new ReadOnlySpan<byte>(roster + rosterSlot * UnitTables.RosterSize + RosterName, NameBytes).CopyTo(name);
                from = $"roster slot {rosterSlot}";
            }
            else
                from = $"battle unit {(source - Ps1Memory.Units) / Ps1Memory.UnitSize}";
        }
        else if ((p[11] & 0x80) != 0)
            from = "the last record";
        name.CopyTo(new Span<byte>(unit + UnitName, NameBytes));
        return $"0xE0: unit id 0x{p[2]:X2} made in slot {slot} from {from}: sprite set 0x{Record[0]:X2}, job 0x{Record[0xA]:X2}, "
               + $"at ({x}, {y}){(made != 0 ? $"; the remaster's unit making answered 0x{made:X}" : "")}";
    }
}
