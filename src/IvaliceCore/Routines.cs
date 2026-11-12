namespace IvaliceCore;

/// <summary>
/// The remaster's damage-formula building blocks, the native rebuilds of the
/// PS1's (PS1 BATTLE.BIN addresses in the comments; remaster RVAs as of
/// 2026-11). Read out of vanilla formulas whose code is checked byte for byte
/// first, and cross-checked against each other.
/// </summary>
/// <remarks>
/// All take no arguments and work on the battle globals (<see cref="Battle"/>),
/// except <see cref="Element"/>. Hit checks return non-zero for a miss.
/// </remarks>
internal sealed unsafe class Routines
{
    /// <summary>Formula 0x01, a plain weapon attack (0x80188B64; 0x307120).</summary>
    public delegate* unmanaged<void> WeaponAttack;
    /// <summary>Physical evade check (0x80188510; 0x306C90).</summary>
    public delegate* unmanaged<int> Evade;
    /// <summary>Magic hit check (0x801885B8; 0x306D70).</summary>
    public delegate* unmanaged<int> MagicHit;
    /// <summary>Weapon XA and YA (0x80185A9C; 0x305A48).</summary>
    public delegate* unmanaged<void> WeaponXa;
    /// <summary>Weapon-chain step after XA (0x80185FA4; 0x305BB8).</summary>
    public delegate* unmanaged<void> AfterWeaponXa;
    /// <summary>Magic step after XA (0x80185FFC; 0x305BE8).</summary>
    public delegate* unmanaged<void> AfterMagicXa;
    /// <summary>Magic modifiers, 0x80188744's four steps (0x80186204, 0x8018631C, 0x80186460, 0x80184964).</summary>
    public delegate* unmanaged<void> MagicMod1, MagicMod2, MagicMod3, MagicMod4;
    /// <summary>HP damage = XA × YA (0x80186568; 0x305F08).</summary>
    public delegate* unmanaged<void> HpDamage;
    /// <summary>Element and weapon-chain finish, given the element (0x80186FD0; 0x305554).</summary>
    public delegate* unmanaged<byte, void> Element;
    /// <summary>On a hit: apply the damage (0x801870FC; 0x306224).</summary>
    public delegate* unmanaged<void> ApplyHit;
    /// <summary>The ability's statuses (0x80187EB4; 0x306944).</summary>
    public delegate* unmanaged<void> Statuses;
    /// <summary>
    /// The status step itself, which <see cref="Statuses"/> wraps to keep the hit and its chance: for an action that's
    /// only a status, it decides the hit (0x80187F24; 0x30698C).
    /// </summary>
    public delegate* unmanaged<void> StatusSet;
    /// <summary>Faith and Innocent (0x80187150; 0x306258).</summary>
    public delegate* unmanaged<void> Faith;
    /// <summary>Damage becomes healing unless the target is Undead (0x80187350; 0x306378).</summary>
    public delegate* unmanaged<void> HealUnlessUndead;
    /// <summary>Attack modifiers: Two Hands, Attack UP, Martial Arts… (0x801885F8; 0x306E34).</summary>
    public delegate* unmanaged<void> AttackMods;
    /// <summary>Critical hits (0x801864F8; 0x305EB4).</summary>
    public delegate* unmanaged<void> Critical;
    /// <summary>The magic finish after HP damage, then the ability's element (0x80186ED0, 0x80186FF8; 0x306134, 0x3061C8).</summary>
    public delegate* unmanaged<void> MagicFinish, MagicElement;
    /// <summary>An item's data, given its id (PS1 item table 0x80062EB8): +5 item type.</summary>
    public delegate* unmanaged<uint, byte*> GetItem;
    /// <summary>A random number, 0 to 0x7FFF (PS1 0x8002230C; 0x30D088).</summary>
    public delegate* unmanaged<int> Random;
    /// <summary>Knock the target back a tile if it can be (0x801843EC; 0x304DC0). Only for single-target actions.</summary>
    public delegate* unmanaged<void> Knockback;
    /// <summary>Formula 0x4E (vanilla Wind Soul's), for ReMixed's 0x18.</summary>
    public delegate* unmanaged<void> Formula4E;
    /// <summary>The weapon chain (0x80188638; 0x306EF0); non-zero for a miss.</summary>
    public delegate* unmanaged<int> WeaponChain;
    /// <summary>Gil Taking's hit roll (0x80188B14; 0x3070E4); non-zero for a miss.</summary>
    public delegate* unmanaged<int> GilRoll;
    /// <summary>Steal gil: (attacker Level + Speed) × target Level, capped by the party's gil (0x80186744; 0x305FA8).</summary>
    public delegate* unmanaged<void> StealGil;
    /// <summary>
    /// Non-zero while the AI simulates an action (PS1 0x8018F5FC): no Brave rolls for
    /// reactions, all-or-nothing status sets, and no miss marker for Gil Taking. (0x186AF80)
    /// </summary>
    public int* Simulating;
    /// <summary>Status-type hit chance and roll (0x8018659C, 0x80187510; 0x305F4C, 0x306408).</summary>
    public delegate* unmanaged<void> StatusChance, StatusRoll;
    /// <summary>Formula 0x58 (vanilla Moldball Virus), for ReMixed's 0x58 when Y is 0.</summary>
    public delegate* unmanaged<void> Formula58;
    /// <summary>The rest of formula 0x24 (Elemental) after XA and YA (0x80188964; 0x306F4C).</summary>
    public delegate* unmanaged<void> ElementalRest;
    /// <summary>Compute an action from a unit's action record and a flag (PS1 0x8018B34C; 0x3099B0).</summary>
    public delegate* unmanaged<byte*, byte, int> ActionRoutine;
    /// <summary>
    /// The globals a sub-action saves, passes and clears (PS1 0x801938C3, 0x801938C1,
    /// 0x801938C6; 0x7B0763, 0x7B0761, 0x7B0768), from formula 0x25's inline copy
    /// of PS1 0x8018AD58.
    /// </summary>
    public byte* SubActionSaved, SubActionParam;
    public int* SubActionCleared;

    /// <summary>The target unit (PS1 0x80192D98). (0x186AF68)</summary>
    public byte** Target;
    /// <summary>The acting unit's own result struct (PS1 0x80192D8C); it's applied when its hit flag is set. (0x186AF60)</summary>
    public byte** AttackerResult;
    /// <summary>The weapon item in use (PS1 0x801938D8). (0x7B077A)</summary>
    public ushort* WeaponItemId;
    /// <summary>The attacker's Faith for this action (PS1 0x801938D3). (0x7B0775)</summary>
    public byte* AttackerFaith;

    public static Routines Find(GameImage image, Battle battle)
    {
        nint Formula(int id) => image.Pointer(battle.FormulaTable[id - 1]);

        // Formula 0x01: if (Evade()) return; if (WeaponChain()) return; Statuses();
        var f01 = GameImage.Calls(Formula(0x01),
            "48 83 EC 28 E8 ?? ?? ?? ?? 85 C0 75 ?? E8 ?? ?? ?? ?? 85 C0 75 ?? E8 ?? ?? ?? ?? 48 83 C4 28 C3",
            "formula 0x01");
        // The weapon chain (0x80188638): XA, charge bonus, then the steps, the hit check, apply.
        var chain = GameImage.Calls(f01[1],
            "48 83 EC 28 E8 ?? ?? ?? ?? 0F B6 05 ?? ?? ?? ?? 66 01 05 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? " +
            "E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8A 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 80 38 00 75 ?? " +
            "B8 01 00 00 00 48 83 C4 28 C3 E8 ?? ?? ?? ?? 48 83 C4 28 E9 ?? ?? ?? ??",
            "the weapon chain");
        // Formula 0x08, magic damage: hit check, XA = MA, YA = Y, magic steps, damage, element, apply, statuses.
        var f08 = GameImage.Calls(Formula(0x08),
            "48 83 EC 28 E8 ?? ?? ?? ?? 85 C0 75 ?? 48 8B 05 ?? ?? ?? ?? 0F B6 48 3F 0F B6 05 ?? ?? ?? ?? " +
            "66 89 05 ?? ?? ?? ?? 66 89 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? " +
            "E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? " +
            "48 8B 05 ?? ?? ?? ?? 80 38 00 74 ?? E8 ?? ?? ?? ??",
            "formula 0x08");
        // Formula 0x4C, magic healing: XA = MA, YA = Y, two magic steps, damage, then heal unless Undead.
        var f4C = GameImage.Calls(Formula(0x4C),
            "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 0F B6 48 3F 0F B6 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? " +
            "66 89 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 83 C4 28 E9 ?? ?? ?? ??",
            "formula 0x4C");

        if (chain[4] != f08[6] || f4C[2] != f08[6] || f4C[0] != f08[2] || f4C[1] != f08[5])
            throw new InvalidDataException("the formulas' building blocks disagree");

        // Weapon XA: reads the attacker's PA, MA and Brave and looks up the weapon's item type.
        const string weaponXa = "40 53 48 83 EC 20 4C 8B 15 ?? ?? ?? ?? 0F B7 0D ?? ?? ?? ?? 45 0F B6 4A 3E " +
                                "45 0F B6 5A 3F 41 0F B6 5A 2B E8 ?? ?? ?? ?? 0F B6 48 05 84 C9";
        var getItem = GameImage.Calls(chain[0], weaponXa, "the weapon XA step");
        // Faith: the attacker's and target's Faith for this action, 100 with Faith status (+0x65 & 0x80).
        const string faith = "4C 8B 05 ?? ?? ?? ?? 45 33 C9 B0 64 41 8A 50 65 84 D2 79 ?? 88 05 ?? ?? ?? ?? " +
                             "8A C8 41 8A 50 65 EB ?? 8A 0D ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ??";
        GameImage.Calls(f08[9], faith, "the Faith step");
        // Formula 0x3C (Chant): the attacker takes Max HP / 5 through its own result struct.
        const string chant = "48 8B 05 ?? ?? ?? ?? 0F B7 48 32 B8 CD CC CC CC F7 E1 48 8B 05 ?? ?? ?? ?? C1 EA 02 " +
                             "66 89 50 06 66 03 D2 C6 40 27 80 C6 00 01 48 8B 05 ?? ?? ?? ?? 66 89 50 08 C6 40 27 40 C3";
        var f3C = Formula(0x3C);
        GameImage.Calls(f3C, chant, "formula 0x3C");

        // Critical hits: 1 in 25, XA up by a random share, then the knockback (only on a crit there).
        var critical = GameImage.Calls(chain[3],
            "48 83 EC 28 E8 ?? ?? ?? ?? 6B C8 64 81 F9 00 00 02 00 7D ?? 48 8B 05 ?? ?? ?? ?? C6 40 01 01 " +
            "E8 ?? ?? ?? ?? 44 0F BF 05 ?? ?? ?? ?? 41 0F AF C0 99 81 E2 FF 7F 00 00 03 C2 C1 F8 0F " +
            "66 44 03 C0 66 44 89 05 ?? ?? ?? ?? E8 ?? ?? ?? ??",
            "the critical-hit step");
        // Knockback: needs the single-target flag, a target that isn't the attacker, and so on.
        GameImage.Calls(critical[2],
            "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20 BD 01 00 00 00 40 38 2D ?? ?? ?? ?? " +
            "0F 85 ?? ?? ?? ?? 48 8B 35 ?? ?? ?? ?? 8A 46 05",
            "the knockback");
        if (critical[0] != critical[1] || (byte**)GameImage.Rip(critical[2] + 38, 3, 7) != (byte**)GameImage.Rip(f08[9] + 40, 3, 7))
            throw new InvalidDataException("the critical-hit step and the knockback disagree");

        // Formula 0x27, Gil Taking: evade, XA = Speed, YA = X, roll, steal; the remaster marks misses.
        var f27 = GameImage.Calls(Formula(0x27),
            "48 83 EC 28 E8 ?? ?? ?? ?? 85 C0 75 ?? 48 8B 05 ?? ?? ?? ?? 0F B6 48 40 0F B6 05 ?? ?? ?? ?? " +
            "66 89 05 ?? ?? ?? ?? 66 89 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 75 ?? E8 ?? ?? ?? ?? " +
            "48 8B 0D ?? ?? ?? ?? 80 39 00 75 ?? 83 3D ?? ?? ?? ?? 00 75 ?? B8 00 20 00 00 66 09 41 12",
            "formula 0x27");
        if (f27[0] != f01[0])
            throw new InvalidDataException("formula 0x27's evade check isn't formula 0x01's");

        // Formula 0x23 (Murasame): the katana-break check, XA = MA, YA = Y, damage, heal unless Undead.
        var f23 = GameImage.Calls(Formula(0x23),
            "48 83 EC 28 E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 0F B6 48 3F 0F B6 05 ?? ?? ?? ?? " +
            "66 89 05 ?? ?? ?? ?? 66 89 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 83 C4 28 E9 ?? ?? ?? ??",
            "formula 0x23");
        // Formula 0x58 (Moldball Virus): XA = MA, YA = X, magic steps, status chance and roll, then its effect.
        var f58 = GameImage.Calls(Formula(0x58),
            "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 0F B6 48 3F 0F B6 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? " +
            "66 89 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? " +
            "48 8B 0D ?? ?? ?? ??",
            "formula 0x58");
        // Formula 0x24 (Elemental): magic hit check, XA = MA, YA = (PA + Y) / 2, then the rest.
        var f24 = GameImage.Calls(Formula(0x24),
            "48 83 EC 28 E8 ?? ?? ?? ?? 85 C0 75 ?? 48 8B 0D ?? ?? ?? ?? 0F B6 41 3F 66 89 05 ?? ?? ?? ?? 0F " +
            "B6 51 3E 0F B6 05 ?? ?? ?? ?? 03 D0 D1 EA 66 89 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 83 C4 28 C3",
            "formula 0x24");
        if (f24[0] != f08[0])
            throw new InvalidDataException("formula 0x24's hit check isn't the magic one");
        // Formula 0x25: when armed, the attacker's plain attack as a sub-action (PS1 0x8018AD58, inlined).
        var f25At = Formula(0x25);
        var f25 = GameImage.Calls(f25At,
            "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20 E8 ?? ?? ?? ?? 33 ED 85 C0 0F 84 ?? ?? ?? ?? 48 8B" +
            " 0D ?? ?? ?? ?? 8A 1D ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01 40 8A B1 A1 01 00 00 0F B7 B9 A2 01 00 00 C6 81 A1 01 0" +
            "0 00 01 66 89 A9 A2 01 00 00 48 81 C1 A0 01 00 00 8A 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 88 1D " +
            "?? ?? ?? ?? 66 89 B8 A2 01 00 00 40 88 B0 A1 01 00 00 89 2D ?? ?? ?? ??",
            "formula 0x25");
        if ((byte**)GameImage.Rip(f25At + 35, 3, 7) != battle.Attacker || (byte**)GameImage.Rip(f25At + 101, 3, 7) != battle.Attacker
            || GameImage.Rip(f25At + 108, 2, 6) != GameImage.Rip(f25At + 42, 2, 6))
            throw new InvalidDataException("formula 0x25's sub-action isn't laid out as this DLL expects");
        // The statuses step keeps the result's hit, chance and miss kind round the status step itself.
        var statuses = GameImage.Calls(f01[2],
            "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 48 8B 05 ?? ?? ?? ?? 40 8A 70 02 0F B7 78 2C 8A 18 E8 ?? ?? ?? ??",
            "the statuses step");
        if (f23[1] != chain[4] || f23[2] != f4C[3] || f58[0] != f08[1] || f58[1] != f08[2] || f58[2] != f08[5])
            throw new InvalidDataException("formulas 0x23 and 0x58 don't use the building blocks this DLL expects");

        if ((byte**)GameImage.Rip(chain[0] + 6, 3, 7) != battle.Attacker
            || (byte**)GameImage.Rip(f08[9], 3, 7) != battle.Attacker
            || (byte**)GameImage.Rip(f3C, 3, 7) != battle.Attacker
            || (byte**)GameImage.Rip(f3C + 42, 3, 7) != battle.TargetResult)
            throw new InvalidDataException("the battle globals and the formulas disagree");

        return new Routines
        {
            AttackMods = (delegate* unmanaged<void>)chain[2],
            Critical = (delegate* unmanaged<void>)chain[3],
            MagicFinish = (delegate* unmanaged<void>)f08[7],
            MagicElement = (delegate* unmanaged<void>)f08[8],
            GetItem = (delegate* unmanaged<uint, byte*>)getItem[0],
            Random = (delegate* unmanaged<int>)critical[0],
            Knockback = (delegate* unmanaged<void>)critical[2],
            Formula4E = (delegate* unmanaged<void>)Formula(0x4E),
            WeaponChain = (delegate* unmanaged<int>)f01[1],
            GilRoll = (delegate* unmanaged<int>)f27[1],
            StealGil = (delegate* unmanaged<void>)f27[2],
            Simulating = (int*)GameImage.Rip(Formula(0x27) + 71, 2, 7),
            StatusChance = (delegate* unmanaged<void>)f58[3],
            StatusRoll = (delegate* unmanaged<void>)f58[4],
            Formula58 = (delegate* unmanaged<void>)Formula(0x58),
            ElementalRest = (delegate* unmanaged<void>)f24[1],
            ActionRoutine = (delegate* unmanaged<byte*, byte, int>)f25[1],
            SubActionSaved = (byte*)GameImage.Rip(f25At + 42, 2, 6),
            SubActionParam = (byte*)GameImage.Rip(f25At + 90, 2, 6),
            SubActionCleared = (int*)GameImage.Rip(f25At + 128, 2, 6),
            WeaponItemId = (ushort*)GameImage.Rip(chain[0] + 13, 3, 7),
            AttackerFaith = (byte*)GameImage.Rip(f08[9] + 20, 2, 6),
            Target = (byte**)GameImage.Rip(f08[9] + 40, 3, 7),
            AttackerResult = (byte**)GameImage.Rip(f3C + 18, 3, 7),
            WeaponAttack = (delegate* unmanaged<void>)Formula(0x01),
            Evade = (delegate* unmanaged<int>)f01[0],
            Statuses = (delegate* unmanaged<void>)f01[2],
            StatusSet = (delegate* unmanaged<void>)statuses[0],
            WeaponXa = (delegate* unmanaged<void>)chain[0],
            AfterWeaponXa = (delegate* unmanaged<void>)chain[1],
            HpDamage = (delegate* unmanaged<void>)chain[4],
            Element = (delegate* unmanaged<byte, void>)chain[5],
            ApplyHit = (delegate* unmanaged<void>)chain[6],
            MagicHit = (delegate* unmanaged<int>)f08[0],
            AfterMagicXa = (delegate* unmanaged<void>)f08[1],
            MagicMod1 = (delegate* unmanaged<void>)f08[2],
            MagicMod2 = (delegate* unmanaged<void>)f08[3],
            MagicMod3 = (delegate* unmanaged<void>)f08[4],
            MagicMod4 = (delegate* unmanaged<void>)f08[5],
            Faith = (delegate* unmanaged<void>)f08[9],
            HealUnlessUndead = (delegate* unmanaged<void>)f4C[3],
        };
    }
}
