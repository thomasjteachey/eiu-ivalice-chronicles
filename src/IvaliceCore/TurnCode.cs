namespace IvaliceCore;

/// <summary>
/// The remaster's code around a unit's turn: movement setup, the end of a turn,
/// battle setup, the effects that follow a move, and reactions to HP damage.
/// Found by their bytes and cross-checked with <see cref="Battle"/> and
/// <see cref="Routines"/>. PS1 BATTLE.BIN addresses in the comments; remaster
/// RVAs in FFT_enhanced.exe as of 2026-11.
/// </summary>
internal sealed unsafe class TurnCode
{
    /// <summary>The battle units, <see cref="Battle.UnitCount"/> × <see cref="Battle.UnitSize"/> bytes (PS1 0x801908CC). (.data 0x1853CE0)</summary>
    public byte* Units { get; private init; }

    /// <summary>
    /// Set up a unit's movement range by battle index (PS1 0x801747xx; 0x27ADB8). It reads
    /// the unit's Move (+0x42) once, caps it at 124 and stores it in <see cref="MoveRange"/>.
    /// </summary>
    public delegate* unmanaged<int, void> MovementSetup { get; private init; }
    /// <summary>The Move that <see cref="MovementSetup"/> works with (PS1 struct +6). (0x1882606)</summary>
    public byte* MoveRange { get; private init; }

    /// <summary>
    /// A unit's end of turn by battle index (PS1 0x80183C6C; 0x310650): CT +20 if it didn't
    /// move, +20 if it didn't act, capped at 60, then clears its turn flag (+0x1B8). Returns 1.
    /// </summary>
    public delegate* unmanaged<int, int> EndOfTurn { get; private init; }

    /// <summary>
    /// Whether a unit's turn is over, after it moves or acts (PS1 0x80183D10; 0x310704):
    /// it moved or can't (Don't Move), and acted or can't (0x30DD1C, PS1 0x8018130C, which
    /// reads Don't Act). The move and act flag setters (0x310548, 0x3105B0) end the turn if so.
    /// </summary>
    public delegate* unmanaged<byte*, int> TurnOver { get; private init; }

    /// <summary>Reset the turn queue; the battle setup calls it once, right before it clears the unit slots. (0x3107EC)</summary>
    public delegate* unmanaged<void> BattleStart { get; private init; }

    /// <summary>
    /// What happens after a unit moves (PS1 0x8017FFC0; 0x271AC0): bit 2 means a post-move
    /// ability (Move-HP Up and the like, PS1 0x80180134, inlined here), 0x400 a status change
    /// for the unit, which the trigger handler shows on it.
    /// </summary>
    public delegate* unmanaged<byte*, int> MoveTriggers { get; private init; }

    /// <summary>
    /// The post-move ability's effect, into the unit's own result (+0x1BE) (PS1 0x8018DD44;
    /// 0x30C5A0). Returns -1 if a status blocks it, else 0.
    /// </summary>
    public delegate* unmanaged<byte*, int> PostMove { get; private init; }

    /// <summary>
    /// Pick the target's reaction to HP damage (PS1 0x8018CB?? to 0x8018CC24; 0x30BBA8):
    /// the first equipped one of 0x1A9, 0x1AA, 0x1AB, 0x1AD, 0x1AE, 0x1B4, 0x1B5, 0x1B6, 0x1BA, 0x1B3.
    /// </summary>
    public delegate* unmanaged<void> HpReactions { get; private init; }
    /// <summary>Whether the target can't react to this action, non-zero if not (0x30AF88).</summary>
    public delegate* unmanaged<int> CannotReact { get; private init; }
    /// <summary>
    /// A percentage roll, (100, chance): non-zero when it fails (0x278E68). Reactions roll
    /// the target's Brave unless the AI is simulating (PS1 0x8018D384).
    /// </summary>
    public delegate* unmanaged<int, int, int> Roll { get; private init; }

    /// <summary>A reaction ability's own effect, for the reaction in <see cref="ReactionId"/> (PS1 0x8018AF3C; 0x3095E4).</summary>
    public delegate* unmanaged<int> ReactionEffect { get; private init; }
    /// <summary>The reaction ability being run (PS1 0x80192D9C). (0x186AFF0)</summary>
    public short* ReactionId { get; private init; }

    /// <summary>
    /// Whether a unit has a status in one of the game's status groups, (unit, group): non-zero
    /// if so (PS1 0x8005E1B0; 0x278FA8). The groups' 5-byte masks are filled at startup
    /// (0x11A6A70): group 4 blocks post-move abilities, group 1 ReMixed's aura neighbours.
    /// </summary>
    public delegate* unmanaged<byte*, int, int> HasStatusIn { get; private init; }

    public static TurnCode Find(GameImage image, Battle battle, Routines routines)
    {
        // Movement setup: unit = units + index × 0x200, and the movement globals.
        var setupBody = image.FindUnique("4C 63 E1 48 8D 05 ?? ?? ?? ?? 45 33 ED 44 88 25 ?? ?? ?? ?? 49 8B FC C7 05 ?? ?? ?? ?? 00 01 01 00");
        var setup = setupBody - 0x1C;
        GameImage.Calls(setup, "48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 41 54 41 55 41 56 41 57 48 83 EC 20 4C 63 E1",
                        "the movement setup's start");
        // ... Move = min(unit +0x42, 124), and the unit's statuses decide the rest.
        var moveRead = image.FindUnique("0F B6 47 42 B9 7C 00 00 00 38 4F 42 0F 47 C1 F6 47 06 C0 88 05 ?? ?? ?? ??");
        if (moveRead < setup || moveRead > setup + 0x538)
            throw new InvalidDataException("the Move cap isn't in the movement setup");

        // End of turn: the unit must exist (+1 != 0xFF) and have the turn (+0x1B8).
        var endOfTurn = image.FindUnique("4C 63 C1 4C 8D 15 ?? ?? ?? ?? 49 C1 E0 09 43 80 7C 10 01 FF 0F 84 ?? ?? ?? ?? 43 80 BC 10 B8 01 00 00 00 0F 84");

        // After a move or an action: set the flags (+0x1B9 moved, +0x1BA acted), then end the turn if it's over.
        var flagSetter = image.FindUnique(
            "80 89 B9 01 00 00 01 45 85 C0 74 ?? 80 B9 B9 01 00 00 01 C6 81 BA 01 00 00 01 75 ?? C6 81 B9 01 00 00 03 " +
            "E8 ?? ?? ?? ?? 85 C0 74 ?? 8B CB E8 ?? ?? ?? ??");
        var afterMove = GameImage.Calls(flagSetter, "80 89 B9 01 00 00 01 45 85 C0 74 ?? 80 B9 B9 01 00 00 01 C6 81 BA 01 00 00 01 75 ?? " +
                                        "C6 81 B9 01 00 00 03 E8 ?? ?? ?? ?? 85 C0 74 ?? 8B CB E8 ?? ?? ?? ??", "the move and act flags");
        GameImage.Calls(afterMove[0], "40 57 48 83 EC 20 4C 8B D1 E8 ?? ?? ?? ?? 41 F6 42 65 08 48 8D 3D ?? ?? ?? ??",
                        "the turn-over check");
        if (afterMove[1] != endOfTurn)
            throw new InvalidDataException("the move and act flags don't end the turn through the end of turn found");

        // Battle setup: reset the turn queue, clear some tables, then mark every unit slot empty (+1 = 0xFF).
        var battleSetup = image.FindUnique(
            "E8 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 44 88 28 48 8D 0D ?? ?? ?? ?? 48 05 D6 32 00 00 48 3B C1 7C EB " +
            "49 8B C5 48 8D 0D ?? ?? ?? ?? 41 BF FF 00 00 00 44 88 BC 08 ?? ?? ?? ??");

        // After a move: the post-move ability bits, inlined at the end of the trigger summary.
        var triggerBits = image.FindUnique("44 8A 86 9D 00 00 00 8A 86 9C 00 00 00 48 8B 6C 24 38 41 22 C6 48 8B 74 24 40 F6 D8 48 8B 7C 24 48 41 8A C0 1B C9 83 E1 22");
        var triggers = triggerBits - 0x130;
        GameImage.Calls(triggers, "48 8B C4 48 89 58 08 48 89 68 10 48 89 70 18 48 89 78 20 41 56 48 83 EC 20 44 0F B6 41 51 48 8B F1 0F B6 51 50 41 B1 41 " +
                        "0F B6 49 4F 41 C1 E8 07 E8 ?? ?? ?? ?? 8B D8 41 BE 01 00 00 00 0F B6 86 B4 01 00 00 F7 D3 C1 EB 1F 84 C0 79",
                        "the trigger summary's start (and its linked-unit check, +0x1B4 & 0x80)");

        // The post-move ability: the unit is the target, its own result (+0x1BE) the result, then clear it.
        var postMove = image.FindUnique(
            "40 53 48 83 EC 20 8A 81 BC 01 00 00 48 8B D9 48 89 0D ?? ?? ?? ?? 48 81 C1 BE 01 00 00 48 89 0D ?? ?? ?? ?? " +
            "88 05 ?? ?? ?? ?? E8 ?? ?? ?? ?? BA 04 00 00 00 48 8B CB E8 ?? ?? ?? ?? 85 C0");

        // Reactions to HP damage: not for formula 7, not if the target can't react, then the reaction bytes.
        var reactions = image.FindUnique("48 83 EC 28 80 3D ?? ?? ?? ?? 07 0F 84 ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 0F 85 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 8A 81 94 00 00 00 A8 10");
        // ... where vanilla checks 0x1AE (Faith Up), after 0x1A9, 0x1AA, 0x1AB and 0x1AD.
        GameImage.Calls(reactions + 0x59, "8A 81 95 00 00 00 84 C0 79 07 B9 AE 01 00 00 EB", "the 0x1AE reaction check");
        // An amount reaction (0x1AE, 0x1B6): needs an amount, then a Brave roll unless simulating.
        var amountReaction = image.FindUnique("48 83 EC 28 80 3D ?? ?? ?? ?? 00 44 0F B7 D9 74 ?? 83 3D ?? ?? ?? ?? 00 75 ?? 48 8B 05 ?? ?? ?? ?? B9 64 00 00 00 0F B6 50 2B E8 ?? ?? ?? ??");

        // A reaction's own effect: 0x1B8 first, then by id.
        var effect = image.FindUnique("48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 48 0F BF 0D ?? ?? ?? ?? B8 B8 01 00 00 40 B7 01 66 3B C8");
        image.Expect("48 8B 1D ?? ?? ?? ?? 0F B7 43 28 66 89 43 0C EB", "Absorb Used MP's effect (MP heal = the queued amount, result +0x28 to +0xC)");

        var code = new TurnCode
        {
            Units = (byte*)GameImage.Rip(setupBody + 3, 3, 7),
            MovementSetup = (delegate* unmanaged<int, void>)setup,
            MoveRange = (byte*)GameImage.Rip(moveRead + 19, 2, 6),
            EndOfTurn = (delegate* unmanaged<int, int>)endOfTurn,
            TurnOver = (delegate* unmanaged<byte*, int>)afterMove[0],
            BattleStart = (delegate* unmanaged<void>)GameImage.Rip(battleSetup, 1, 5),
            MoveTriggers = (delegate* unmanaged<byte*, int>)triggers,
            PostMove = (delegate* unmanaged<byte*, int>)postMove,
            HpReactions = (delegate* unmanaged<void>)reactions,
            CannotReact = (delegate* unmanaged<int>)GameImage.Rip(reactions + 17, 1, 5),
            Roll = (delegate* unmanaged<int, int, int>)GameImage.Rip(amountReaction + 42, 1, 5),
            ReactionEffect = (delegate* unmanaged<int>)effect,
            ReactionId = (short*)GameImage.Rip(effect + 15, 4, 8),
            HasStatusIn = (delegate* unmanaged<byte*, int, int>)GameImage.Rip(postMove + 55, 1, 5),
        };

        // Cross-checks, so a game update that moves things fails here.
        if ((byte*)GameImage.Rip(endOfTurn + 3, 3, 7) != code.Units
            || (byte*)(image.ImageStart + *(int*)(battleSetup + 53) - 1) != code.Units)
            throw new InvalidDataException("the movement setup, the end of turn and the battle setup disagree on the unit array");
        if ((byte**)GameImage.Rip(postMove + 15, 3, 7) != routines.Target
            || (byte**)GameImage.Rip(reactions + 30, 3, 7) != routines.Target)
            throw new InvalidDataException("the post-move ability, the reactions and the formulas disagree on the target");
        if ((byte**)GameImage.Rip(postMove + 29, 3, 7) != battle.TargetResult)
            throw new InvalidDataException("the post-move ability and the HP damage step disagree on the result struct");
        if ((byte*)GameImage.Rip(reactions + 4, 2, 7) != battle.EffectiveFormula)
            throw new InvalidDataException("the reactions and the action setup disagree on the formula");
        if ((int*)GameImage.Rip(amountReaction + 17, 2, 7) != routines.Simulating)
            throw new InvalidDataException("the reactions and Gil Taking disagree on the simulation flag");
        return code;
    }
}
