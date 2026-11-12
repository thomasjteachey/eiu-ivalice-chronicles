namespace IvaliceCore;

/// <summary>
/// The remaster's job and skillset tables and the roster, found by the code that reads them. RVAs as
/// of 2026-11 (Ivalice ReMixed's notes on the remaster).
/// </summary>
internal sealed unsafe class UnitTables
{
    public const int JobSize = 0x31, JobCount = 174, RosterSlots = 54, RosterSize = 0x258;
    /// <summary>A skillset's ability slots that <see cref="SkillsetAbility"/> answers for (16 actions, 6 R/S/M).</summary>
    public const int SkillsetSlots = 22;

    /// <summary>The job data, <see cref="JobSize"/> bytes a job: +0 its skillset (PS1 SCUS 0x800610B8, 48 bytes). (.rdata 0x785E30)</summary>
    public byte* Jobs { get; private init; }
    /// <summary>The ability in a skillset's slot, 0 for none (PS1 table 0x80064A94, 25 bytes a skillset). (0x275860)</summary>
    public delegate* unmanaged<int, int, ushort> SkillsetAbility { get; private init; }
    /// <summary>The roster: <see cref="RosterSlots"/> records of <see cref="RosterSize"/> bytes; +0x01 is 0xFF for an empty slot. (.data 0x11A7D10)</summary>
    public byte* Roster { get; private init; }

    public int SkillsetOf(int job) => Jobs[job * JobSize];

    public static UnitTables Find(GameImage image)
    {
        // A battle unit's random secondary: a random job's skillset (job data + 0x31 × job).
        var secondary = image.FindUnique(
            "8A 47 0B 3C FE 75 44 F6 43 06 20 75 38 48 8B CB E8 ?? ?? ?? ?? 84 C0 74 11 0F B6 C0 48 6B C8 31 " +
            "42 8A 84 29 ?? ?? ?? ??");
        // The slot lookup: a generic skillset (under 0xB0), slot up to 0x15, 25 bytes a skillset.
        var lookup = image.FindUnique(
            "41 B8 B0 00 00 00 4C 63 CA 41 3B C8 7D ?? 41 83 F9 15 0F 8F ?? ?? ?? ?? 48 63 C1 4C 8D 15 ?? ?? ?? ?? " +
            "4C 6B C0 19");
        // The party level's roster walk: slot + 1, + 0x1D the level, 54 slots of 0x258.
        var roster = image.FindUnique(
            "48 63 C2 4D 8D 83 ?? ?? ?? ?? 48 69 C8 58 02 00 00 49 8D 83 ?? ?? ?? ?? 4C 03 C1 48 03 C1 44 38 30");
        var tables = new UnitTables
        {
            Jobs = (byte*)(image.ImageStart + *(int*)(secondary + 36)),
            SkillsetAbility = (delegate* unmanaged<int, int, ushort>)lookup,
            Roster = (byte*)(image.ImageStart + *(int*)(roster + 0x14) - 1),
        };
        if ((byte*)(image.ImageStart + *(int*)(roster + 6)) != tables.Roster + 0x1D)
            throw new InvalidDataException("the roster isn't where this DLL expects");
        return tables;
    }
}
