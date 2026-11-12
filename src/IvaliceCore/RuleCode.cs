namespace IvaliceCore;

/// <summary>
/// Remaster code that ReMixed's battle rules hook (<see cref="Patches.Rules"/>): the evade
/// setup, the Defense UP and Magic Defense UP steps, a unit's tile info, and queueing an
/// action. Found by their bytes and cross-checked with <see cref="Battle"/> and
/// <see cref="Routines"/>. PS1 BATTLE.BIN addresses in the comments; remaster RVAs in
/// FFT_enhanced.exe as of 2026-11.
/// </summary>
internal sealed unsafe class RuleCode
{
    /// <summary>
    /// The target's evade values for an evade check (PS1 0x8018520C..; 0x3055F0). Both evade
    /// checks (0x306C90, 0x3072F8) call it, then zero <see cref="Evades"/> if the attacker
    /// has Concentrate (PS1 0x801852E4, inlined).
    /// </summary>
    public delegate* unmanaged<void> EvadeSetup { get; private init; }
    /// <summary>The four evade bytes (PS1 0x801938DE). (0x7B0781)</summary>
    public uint* Evades { get; private init; }

    /// <summary>The target's Defense UP: XA × 2/3 (PS1 0x801862CC; 0x305D40).</summary>
    public delegate* unmanaged<void> DefenseUp { get; private init; }
    /// <summary>The target's Magic Defense UP: XA × 2/3 (PS1 0x8018631C; 0x305D98).</summary>
    public delegate* unmanaged<void> MagicDefenseUp { get; private init; }

    /// <summary>
    /// A unit's tile info by battle index, into an 8-byte buffer it returns (PS1 0x8017C158;
    /// 0x281248). +6 is the unit's effective height: the tile's, plus depth, slope and Float.
    /// </summary>
    public delegate* unmanaged<byte*, int, byte*> TileInfo { get; private init; }

    /// <summary>
    /// Queue a unit's action, (20-byte action record: +0 battle index, +1 skillset, +2 ability;
    /// the unit's record to fill; commit) (PS1 0x8017C45C; 0x281488). For a charged ability:
    /// CT from the ability's attributes, halved by Short Charge, zeroed by Non-charge; when
    /// committing, the unit's CT (+0x18D), "acted", and Charging or Performing.
    /// </summary>
    public delegate* unmanaged<byte*, byte*, int, int> QueueAction { get; private init; }
    /// <summary>An ability's live 20-byte attribute record (with the mod loader's overrides), null above 0x16F (0x2BB060).</summary>
    public delegate* unmanaged<int, byte*> GetAbilityAction { get; private init; }
    /// <summary>Set once <see cref="GetAbilityAction"/> has built the live table from the game's data, on first use. (0xD407B7)</summary>
    public byte* AbilitiesReady { get; private init; }

    /// <summary>
    /// The status step itself (PS1 0x80187F24; 0x30698C): applies the action's status set to
    /// the result, by its flags (+0 of <see cref="StatusCopy"/>). The outer step (0x306944,
    /// <see cref="Routines.Statuses"/>) saves and restores the result's hit, type and status
    /// chance around it; status-only formulas call this one directly.
    /// </summary>
    public delegate* unmanaged<void> StatusStep { get; private init; }
    /// <summary>The action's copy of its 6-byte status set: flags (0x80 all or nothing, 0x40 random, 0x20 separate, 0x10 cancel), then 5 status bytes (PS1 0x80193906). (0x7B07B0)</summary>
    public byte* StatusCopy { get; private init; }
    /// <summary>The id of the set the action setup copied (the ability's, +0xB of its attributes; formula code may change it). (0x7B077D)</summary>
    public byte* StatusId { get; private init; }
    /// <summary>The status sets, 6 bytes each by id, as the mod loader's ItemOptionsData leaves them (PS1 SCUS 0x800664D4..). (.data 0x80FBA0)</summary>
    public byte* StatusSets { get; private init; }
    /// <summary>
    /// The 19% roll for a hit's statuses (PS1 0x801884C0 with 0x80187690; 0x306C10): sets
    /// result +0x12 &amp; 0x200 on success, and returns non-zero to skip the statuses. The
    /// weapon chain ends in it, and so do the formula steps that apply a hit.
    /// </summary>
    public delegate* unmanaged<int> HitStatusRoll { get; private init; }

    public static RuleCode Find(GameImage image, Battle battle, Routines routines)
    {
        // The outer status step: keep the result's hit, type and status chance, run the step, put them back.
        var step = GameImage.Calls((nint)routines.Statuses,
            "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 48 8B 05 ?? ?? ?? ?? 40 8A 70 02 0F B7 78 2C 8A 18 E8 ?? ?? ?? ??",
            "the outer status step")[0];
        // ... the step: its frame, then the set's flags from the action's copy.
        GameImage.Calls(step, "48 8B C4 48 89 58 08 48 89 68 10 48 89 70 18 48 89 78 20 41 54 41 56 41 57 48 81 EC D0 00 00 00",
                        "the status step's start");
        GameImage.Calls(step + 0x44, "8A 0D ?? ?? ?? ?? 48 8B 1D ?? ?? ?? ?? F6 C1 20", "the status step's flags");
        // Action setup: copy the action's status set (6 bytes from the set table) into its copy.
        var setCopy = image.FindUnique(
            "0F B6 05 ?? ?? ?? ?? 4D 8D 84 24 ?? ?? ?? ?? 0F 47 FB 41 8B CF BA 06 00 00 00 40 80 FF 03 44 8B CA " +
            "66 0F 45 C8 0F B7 C1 48 8D 0C 40 4D 8D 04 48 48 8D 0D ?? ?? ?? ?? E8");
        // The hit's 19% status roll: not while simulating; the result's 0x200 bit.
        var roll = image.FindUnique("40 53 48 83 EC 20 83 3D ?? ?? ?? ?? 00 BB 00 02 00 00 4C 8B 1D ?? ?? ?? ?? 75 ?? B8 DB 00 00 00");

        // Evade check: the evade setup, then Concentrate zeroes the evades (two copies, one per evade check).
        var evade = image.FindUnique("E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 33 DB F6 80 99 00 00 00 01 74 06 89 1D ?? ?? ?? ?? F6 40 62 30");
        var evade2 = image.FindUnique("E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? F6 80 99 00 00 00 01 74 07 83 25 ?? ?? ?? ?? 00 F6 40 62 30");

        // Defense UP and Magic Defense UP on the target: XA × 2/3 (and the remaster's own ×3/2 after).
        var defenseUp = image.FindUnique("4C 8B 05 ?? ?? ?? ?? 41 F6 80 99 00 00 00 08 74 20 0F BF 0D ?? ?? ?? ?? B8 56 55 55 55");
        var magicDefenseUp = image.FindUnique("4C 8B 05 ?? ?? ?? ?? 41 F6 80 99 00 00 00 02 74 20 0F BF 0D ?? ?? ?? ?? B8 56 55 55 55");

        // Monster Skill's neighbour check (rbx = unit + 0x4F, so +0x16D is the battle index): the
        // neighbour's tile info, then its height within 5 of the unit's.
        var heightCheck = image.FindUnique("0F B6 93 6D 01 00 00 48 8D 4C 24 20 E8 ?? ?? ?? ?? 0F B6 48 06 41 8B C6 2B C1 83 C0 05 83 F8 0A");

        // Queueing an action: copy the 20-byte record, then the unit by its battle index (+0).
        var queueBody = image.FindUnique("45 8B F0 41 BD 14 00 00 00 4C 8B C1 4C 8B FA 45 8B CD 48 8D 4D E0 41 8B D5 E8 ?? ?? ?? ?? 0F B6 45 E0 3C 15");
        var queue = queueBody - 0x25;
        GameImage.Calls(queue, "48 89 5C 24 18 55 56 57 41 54 41 55 41 56 41 57 48 8B EC 48 83 EC 40", "the action queue's start");
        // ... a charged ability's CT: attributes +0xC & 0x7F, the no-charge flag (+5 & 4), the unit's supports (+0x9B).
        var charge = image.FindUnique("41 0F BF CC E8 ?? ?? ?? ?? 41 B8 04 00 00 00 8A 48 0C 8A 50 05 80 E1 7F 0F B6 86 9B 00 00 00");
        if (charge < queue || charge > queue + 0x500)
            throw new InvalidDataException("the charge time isn't in the action queue");

        var getAbility = GameImage.Rip(charge + 4, 1, 5);
        GameImage.Calls(getAbility, "40 53 48 83 EC 20 48 63 D9 81 FB 6F 01 00 00 77 ?? 80 3D ?? ?? ?? ?? 00 75 ?? E8 ?? ?? ?? ?? " +
                        "C6 05 ?? ?? ?? ?? 01 48 8D 0C 9B 48 8D 05 ?? ?? ?? ?? 48 8D 04 88", "GetAbilityAction");

        var code = new RuleCode
        {
            EvadeSetup = (delegate* unmanaged<void>)GameImage.Rip(evade, 1, 5),
            Evades = (uint*)GameImage.Rip(evade + 23, 2, 6),
            DefenseUp = (delegate* unmanaged<void>)defenseUp,
            MagicDefenseUp = (delegate* unmanaged<void>)magicDefenseUp,
            TileInfo = (delegate* unmanaged<byte*, int, byte*>)GameImage.Rip(heightCheck + 12, 1, 5),
            QueueAction = (delegate* unmanaged<byte*, byte*, int, int>)queue,
            GetAbilityAction = (delegate* unmanaged<int, byte*>)getAbility,
            AbilitiesReady = (byte*)GameImage.Rip(getAbility + 0x11, 2, 7),
            StatusStep = (delegate* unmanaged<void>)step,
            StatusCopy = (byte*)GameImage.Rip(step + 0x44, 2, 6),
            StatusSets = (byte*)(image.ImageStart + *(int*)(setCopy + 11)),
            StatusId = (byte*)GameImage.Rip(setCopy, 3, 7),
            HitStatusRoll = (delegate* unmanaged<int>)roll,
        };

        // Cross-checks, so a game update that moves things fails here.
        if ((nint)code.EvadeSetup != GameImage.Rip(evade2, 1, 5) || (uint*)GameImage.Rip(evade2 + 21, 2, 7) != code.Evades)
            throw new InvalidDataException("the two evade checks disagree on the evade setup or the evade values");
        if ((byte*)GameImage.Rip(setCopy + 48, 3, 7) != code.StatusCopy)
            throw new InvalidDataException("the action setup and the status step disagree on the status-set copy");
        if ((int*)GameImage.Rip(roll + 6, 2, 7) != routines.Simulating || (byte**)GameImage.Rip(roll + 18, 3, 7) != battle.TargetResult)
            throw new InvalidDataException("the hit's status roll and the formulas disagree on the simulation flag or the result");
        if ((byte*)GameImage.Rip(getAbility + 0x1F, 2, 7) != code.AbilitiesReady)
            throw new InvalidDataException("GetAbilityAction's flag isn't where this DLL expects");
        if ((byte**)GameImage.Rip(evade + 5, 3, 7) != battle.Attacker)
            throw new InvalidDataException("the evade check and the HP damage step disagree on the attacker");
        if ((byte**)GameImage.Rip(defenseUp, 3, 7) != routines.Target || (byte**)GameImage.Rip(magicDefenseUp, 3, 7) != routines.Target
            || (short*)GameImage.Rip(defenseUp + 17, 3, 7) != battle.XA || (short*)GameImage.Rip(magicDefenseUp + 17, 3, 7) != battle.XA)
            throw new InvalidDataException("Defense UP, Magic Defense UP and the formulas disagree on the target or XA");
        return code;
    }
}
