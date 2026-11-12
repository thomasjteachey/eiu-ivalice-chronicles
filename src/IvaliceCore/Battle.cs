namespace IvaliceCore;

/// <summary>
/// The remaster's battle-action globals, found by the code that uses them.
/// It's a rebuild of the PS1 game, so most of these are the PS1's (BATTLE.BIN
/// addresses in the comments), with the layout of some structures shifted.
/// Addresses noted are RVAs in FFT_enhanced.exe as of 2026-11 (Ivalice ReMixed's notes on the remaster).
/// </summary>
internal sealed unsafe class Battle
{
    /// <summary>The current ability's 20-byte attribute record (PS1 0x801938F0, 14 bytes): +7 element, +8 formula, +9 X, +10 Y. (0x7B0794)</summary>
    public byte* Ability { get; private init; }
    /// <summary>The weapon's 8-byte record (PS1 0x801938FE): +1 flags, +2 formula, +4 WP, +6 element. (0x7B07A8)</summary>
    public byte* Weapon { get; private init; }
    /// <summary>XA and YA, the damage factors (PS1 0x801938CE/D0). (0x7B0770, 0x7B0772)</summary>
    public short* XA { get; private init; }
    public short* YA { get; private init; }
    /// <summary>The formula being run (PS1 0x801938F8 as the dispatch reads it). (0x7B0788)</summary>
    public byte* EffectiveFormula { get; private init; }
    /// <summary>The current ability id (PS1 0x801938D6). (0x7B0778)</summary>
    public ushort* AbilityId { get; private init; }
    /// <summary>Set when Two Hands applies to this action (PS1 0x801938D9). (0x7B077C)</summary>
    public byte* TwoHands { get; private init; }
    /// <summary>Pointer to the target's action-result struct (PS1 0x80192D90): +0 hit, +6 HP damage (PS1 +4), +0x27 flags. (0x186AF70)</summary>
    public byte** TargetResult { get; private init; }
    /// <summary>Pointer to the acting unit (PS1 0x80192D94): +0x20..+0x26 hand slots (u16), +0x9A supports (2 = Two Hands). (0x186AF78)</summary>
    public byte** Attacker { get; private init; }
    /// <summary>Formula routines; entry 0 is formula 1 (PS1 table 0x8018F610 + 4 × id). (.rdata 0x682BD0)</summary>
    public nint* FormulaTable { get; private init; }
    /// <summary>The abilities' 8-byte common records (PS1 SCUS 0x8005EBF0): +4..+7 AI flags. (.data 0x787F80)</summary>
    public byte* AbilityCommon { get; private init; }

    // Result struct fields (PS1: +4, +6, +8, +0xA, +0x25).
    public const int HpDamage = 6, HpHeal = 8, MpDamage = 0xA, MpHeal = 0xC, ResultFlags = 0x27;
    /// <summary>The result's status hit chance (PS1 +0x2A; the status step saves and restores it).</summary>
    public const int ResultStatusChance = 0x2C;
    public const int TargetHpDamage = HpDamage;
    /// <summary>
    /// Unit fields (PS1 in brackets): Brave (+0x24), Max HP (+0x2A), PA (+0x36),
    /// MA (+0x37), map X and Y (+0x47, +0x48). Each one appears literally in a
    /// pattern the DLL checks, so a layout change fails at startup.
    /// </summary>
    public const int UnitBrave = 0x2B, UnitHp = 0x30, UnitMaxHp = 0x32, UnitMp = 0x34, UnitPa = 0x3E, UnitMa = 0x3F,
                     UnitSpeed = 0x40, UnitX = 0x4F, UnitY = 0x50;
    /// <summary>The unit's EXP and character level (PS1 +0x21, +0x22).</summary>
    public const int UnitExp = 0x28, UnitLv = 0x29;
    /// <summary>The result's EXP and JP, bytes, as Move-Get Exp and JP give them (PS1 +0x28, +0x29).</summary>
    public const int ResultExp = 0x2A, ResultJp = 0x2B;
    /// <summary>
    /// The result's stat changes (PS1 +0x12): Speed, CT, PA, MA, Brave, Faith, a byte each; bit 7
    /// raises the stat by the low 7 bits, otherwise they lower it (Stall writes CT 0x7F).
    /// </summary>
    public const int ResultStatChanges = 0x14;
    /// <summary>The unit's queued action record (PS1 +0x16E): +1 type, +2 ability id (16-bit).</summary>
    public const int UnitAction = 0x1A0;
    /// <summary>Bit 7: the unit stands on a tile's upper level (PS1 +0x49).</summary>
    public const int UnitLevel = 0x51;
    /// <summary>
    /// The unit's queued action's target (PS1 +0x178 to +0x17E): type (5 = a panel),
    /// target unit index, then the panel's x, level and y as 16-bit values.
    /// </summary>
    public const int UnitTargetType = 0x1AA, UnitTargetIndex = 0x1AB, UnitTargetX = 0x1AC, UnitTargetLevel = 0x1AE, UnitTargetY = 0x1B0;
    /// <summary>The unit's team, bits 0x30 (PS1 +0x1BA; +5 holds the current one). Tested with 0x30 and 0x38 like the PS1's.</summary>
    public const int UnitTeam = 0x1EE;
    /// <summary>The result's second flags word (PS1 +0x10): 0x2000 missed, 0x4000 knocked back.</summary>
    public const int ResultFlags2 = 0x12;
    /// <summary>Support abilities, 4 bytes from 0x1C6 down (PS1 +0x8F): +2 & 2 Two Hands, +2 & 4 Maintenance.</summary>
    public const int UnitSupports = 0x98;
    /// <summary>
    /// The item types the unit can equip, 4 bytes (PS1 +0x4A): type t is bit 0x80 >> (t &amp; 7) of byte t >> 3,
    /// so byte 0 is (none), Knife, Ninja Blade, Sword, Knight Sword, Katana, Axe, Rod, then Staff…Spear,
    /// Pole…Armor, Clothing…Perfume.
    /// </summary>
    public const int UnitEquipTypes = 0x52;
    /// <summary>The unit's secondary skillset (PS1 +0x13).</summary>
    public const int UnitSecondary = 0x13;
    /// <summary>Reaction abilities, 4 bytes from 0x1A6 (PS1 +0x8B), and movement abilities, 3 bytes from 0x1E6 (PS1 +0x93).</summary>
    public const int UnitReactions = 0x94, UnitMovement = 0x9C;
    /// <summary>The battle units: 21 slots of 0x200 bytes (PS1 0x1C0); +1 is 0xFF for an empty slot.</summary>
    public const int UnitCount = 21, UnitSize = 0x200;
    /// <summary>Move (PS1 +0x3A), Jump (PS1 +0x3B) and Max MP (PS1 +0x2E).</summary>
    public const int UnitMove = 0x42, UnitJump = 0x43, UnitMaxMp = 0x36;
    /// <summary>The unit's current statuses, 5 bytes (PS1 +0x58): +4 & 8 Don't Move, +4 & 4 Don't Act.</summary>
    public const int UnitStatuses = 0x61;
    /// <summary>Bit 7: the unit is linked to another one, such as a rider and its mount (PS1 +0x182; low 5 bits its index).</summary>
    public const int UnitLinked = 0x1B4;
    /// <summary>Set while it's the unit's turn; the end of turn clears it (PS1 +0x186).</summary>
    public const int UnitTurn = 0x1B8;
    /// <summary>Moved (bit 1; 3 once it has moved and then acted) and acted, this turn (PS1 +0x187, +0x188).</summary>
    public const int UnitMoved = 0x1B9, UnitActed = 0x1BA;
    /// <summary>The unit's current team, bits 0x30 (PS1 +5): what the Monster Skill and aura neighbour checks compare.</summary>
    public const int UnitCurrentTeam = 5;
    /// <summary>The unit's battle index (PS1 +0x18A).</summary>
    public const int UnitIndex = 0x1BC;
    /// <summary>The unit's own result struct, for effects on itself such as Move-HP Up (PS1 +0x18C).</summary>
    public const int UnitResult = 0x1BE;
    /// <summary>
    /// A second copy of the unit's 5 status bytes (PS1 +0x1BB, same bits as +0x61): the one
    /// the game checks to remove Transparent after an action, and ReMixed's cures read.
    /// </summary>
    public const int UnitStatusCopy = 0x1EF;
    /// <summary>The result's statuses to add and to remove, 5 bytes each (PS1 +0x1B, +0x20); flags bit 8 applies them.</summary>
    public const int ResultStatusAdded = 0x1D, ResultStatusRemoved = 0x22;
    /// <summary>The reaction the result queues, and its amount (PS1 +0x0E, +0x26), both 16-bit.</summary>
    public const int ResultReaction = 0x10, ResultReactionAmount = 0x28;

    public static Battle Find(GameImage image)
    {
        // Action setup: memcpy(ability block, GetAbilityAction(id), 20); memcpy(weapon block, weapon, 8).
        var setup = image.FindUnique("BA 14 00 00 00 48 8D 0D ?? ?? ?? ?? 44 8B CA 4C 8B C0 E8 ?? ?? ?? ?? B8 08 00 00 00 48 8D 0D ?? ?? ?? ??");
        // HP damage = XA × YA, into the target's result.
        var damage = image.FindUnique("0F BF 05 ?? ?? ?? ?? 0F BF 0D ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ?? 0F AF C8 B8 FF 7F 00 00 41 C6 40 27 80");
        // Attack modifiers: Two Hands doubles XA, then the attacker's Attack UP.
        var twoHands = image.FindUnique("8A 05 ?? ?? ?? ?? 24 05 3C 04 75 ?? 80 3D ?? ?? ?? ?? 00 74 ??");
        var attacker = image.FindUnique("4C 8B 05 ?? ?? ?? ?? 41 F6 80 99 00 00 00 10");
        // Action setup: ability 0x166 keeps its own status id.
        var abilityId = image.FindUnique("B8 66 01 00 00 66 39 05 ?? ?? ?? ?? 74 ?? 8A 4E 0B");
        // Formula dispatch: call [table + formula × 8 - 8].
        var dispatch = image.FindUnique("48 83 EC 28 0F B6 05 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? FF 54 C1 F8");
        // Action setup: store the formula to run, compute the action, check the target's hit flag.
        var formula = image.FindUnique("40 88 3D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 44 38 39");
        // Action setup: targeting bits from the ability's common record, [image + id × 8 + table + 4].
        var common = image.FindUnique("48 0F BF C7 41 8A 8C C4 ?? ?? ?? ?? 80 E1 03 88 4A 1A");

        var battle = new Battle
        {
            Ability = (byte*)GameImage.Rip(setup + 5, 3, 7),
            Weapon = (byte*)GameImage.Rip(setup + 28, 3, 7),
            XA = (short*)GameImage.Rip(damage, 3, 7),
            YA = (short*)GameImage.Rip(damage + 7, 3, 7),
            TargetResult = (byte**)GameImage.Rip(damage + 14, 3, 7),
            TwoHands = (byte*)GameImage.Rip(twoHands + 12, 2, 7),
            Attacker = (byte**)GameImage.Rip(attacker, 3, 7),
            FormulaTable = (nint*)GameImage.Rip(dispatch + 11, 3, 7),
            AbilityId = (ushort*)GameImage.Rip(abilityId + 5, 3, 7),
            EffectiveFormula = (byte*)GameImage.Rip(formula, 3, 7),
            // The displacement is image-relative (r12 holds the image base) and points at byte 4.
            AbilityCommon = (byte*)(image.ImageStart + *(int*)(common + 8) - 4),
        };
        // Map tile of a unit: y × map width + height level × 256 + x, so X is +0x4F and Y +0x50.
        image.Expect("0F B6 4A 50 0F B6 05 ?? ?? ?? ?? 0F AF C8 0F B6 42 51 C1 E8 07 C1 E0 08 03 C8 0F B6 42 4F",
                     "the unit's map position");

        image.Expect("F6 81 EE 01 00 00 30", "the unit's team (+0x1EE & 0x30)");
        image.Expect("0F B7 40 30 33 D2 0F B7 49 32 6B C0 64 FF C1", "the unit's HP and Max HP (+0x30, +0x32)");
        // Move-MP Up: (Max MP + 9) / 10.
        image.Expect("0F B7 4B 36 B8 CD CC CC CC 83 C1 09", "the unit's Max MP (+0x36)");
        // After an action: if the status copy has Transparent (byte 2, 0x10), remove it (result +0x24) with flags 8.
        image.Expect("F6 81 F1 01 00 00 10 74 ?? 80 79 2E 00 74 ?? C6 40 24 10 C6 40 27 08",
                     "the unit's status copy (+0x1EF) and the result's statuses to remove (+0x22)");
        // Monster Skill's neighbour check: the neighbour's support byte (unit +0x4F base: +0x4C is +0x9B), then its current team (+5).
        image.Expect("38 43 4C 7D ?? 40 8A C5 32 43 B6 A8 30", "the unit's current team (+5 & 0x30)");
        // An amount reaction: the reaction id and its amount into the result.
        image.Expect("48 8B 0D ?? ?? ?? ?? 66 44 89 59 10 0F B6 05 ?? ?? ?? ?? 66 89 41 28",
                     "the result's queued reaction (+0x10) and amount (+0x28)");

        // Cross-checks between the patterns, so a game update that moves things fails here.
        if ((byte**)GameImage.Rip(formula + 12, 3, 7) != battle.TargetResult)
            throw new InvalidDataException("the action setup and the HP damage step disagree on the result struct");
        if ((byte*)GameImage.Rip(twoHands, 2, 6) != battle.Weapon + 1)
            throw new InvalidDataException("the weapon block and the Two Hands check disagree");
        if ((byte*)GameImage.Rip(dispatch + 4, 3, 7) != battle.Weapon + 2)
            throw new InvalidDataException("the weapon block and the formula dispatch disagree");
        return battle;
    }
}
