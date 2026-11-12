namespace EventInstructionUpgrade;

/// <summary>
/// The Event Instruction Upgrade's instructions (Xifanie's PS1 hack, as ReMixed has it), run for the remaster
/// (docs/INSTRUCTIONS.md). The remaster's interpreter keeps running the instructions both games share
/// (dialogue, camera, units, vanilla's variable arithmetic and comparisons); <see cref="EventHooks"/> hands each
/// instruction of an upgrade event to <see cref="Step"/> first, which runs it when it's one of the upgrade's and
/// returns where to go on. Offsets are from
/// the start of the event's buffer, its code from <see cref="Start"/>. The control flow follows ReMixed's interpreter (BATTLE.BIN 0x801588BC) exactly,
/// its scanner's quirks included; PS1 addresses go through <see cref="Ps1Memory"/>.
/// </summary>
internal sealed unsafe class EventScript(Func<int, int> get, Action<int, int> set, Ps1Memory memory, Action<string> note)
{
    /// <summary>ReMixed's parameter bytes per opcode (BATTLE.BIN 0x8014D170), with MESSAGE (0x10) and 0x51 as the build
    /// converts them (docs/WRITING-EVENTS.md, "Messages"): the remaster's enhanced layout, 3 and 2 bytes longer
    /// for its 32-bit line ids.</summary>
    internal static readonly byte[] Lengths = Widened(Convert.FromHexString(
        "000001000001020000000100010101030E050202010000010610050E01000005" +
        "0602030300040200080200000706080400050705020402020200000C02020904" +
        "0504000102030208000000000201030101050207020200000301010103010106" +
        "02030601020000020308050502020C020402000E060606000204020200010404" +
        "0303000501010304000000000603000103030502020000020202000201030203" +
        "0000000000000000000000000404040404040404040404040404040404040203" +
        "1005020205020303000000000103020401010101010100000101000000000000" +
        "0C01010001020203050504040102020200020000000000000000000000000000"));

    private static byte[] Widened(byte[] lengths)
    {
        lengths[0x10] += 3;
        lengths[0x51] += 2;
        return lengths;
    }

    /// <summary>The opcodes the upgrade's interpreter handles before vanilla's (or rewrote in place).</summary>
    internal static readonly bool[] Ours = BuildOurs();

    /// <summary>The opcodes this port runs; the rest of <see cref="Ours"/> are passed over (noted once each).</summary>
    private static readonly HashSet<int> Done =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x14, 0x8D,
        0x9A, 0x9B, 0x9C, 0x9D, 0x9E,
        0xAC, 0xAD, 0xAE, 0xAF, 0xBF, 0xC1, 0xC2, 0xC3, 0xC4, 0xC7, 0xCD, 0xCE, 0xCF, 0xE0, 0xE6, 0xE7, 0xE8, 0xE9, 0x2F,
    ];

    /// <summary>Where the code starts in its buffer (after a u32), and where CALL and GOTO look from (the PS1's 4).</summary>
    internal const int Start = 4;

    /// <summary>0x14's music change: vanilla's CallFunction 0xC with the track given (the remaster's in
    /// <see cref="RemixedEvents"/>).</summary>
    public Action<int>? PlayMusic { get; set; }

    /// <summary>0x8D's items: an item's owned count, setting it, and its count owned and equipped.</summary>
    public (Func<int, int> Owned, Action<int, int> SetOwned, Func<int, int> Total)? Items { get; set; }

    /// <summary>LoadConditionals' (0xCE) presets: a mod's battle event conditions per event number, 0x220 bytes each
    /// (0x20 of offsets, 0x200 of records), as ReMixed's disc has them.</summary>
    public byte[]? Presets { get; set; }
    internal const int PresetBytes = 0x220, PresetOffsets = 0x20, PresetRecords = 0x200;

    /// <summary>Native routines (Routine, 0xCF's addresses) a mod has in C# (<see cref="Api.AddRoutine"/>).</summary>
    public Dictionary<uint, Action> Natives { get; } = [];

    /// <summary>0xE8 and 0xE9's map tile at x, y and level (8 bytes; 0 off the map).</summary>
    public Func<int, int, int, nint>? TileAt { get; set; }

    /// <summary>0xE0's unit making (<see cref="CreateUnit.Make"/>): given the instruction's 12 parameters, what it
    /// did, for the log.</summary>
    public Func<nint, string>? MakeUnit { get; set; }

    private readonly ushort[] _calls = new ushort[128]; // ReMixed's call stack (0x801502A0, u16 offsets)
    private int _depth;
    private readonly HashSet<int> _passed = [];

    private static bool[] BuildOurs()
    {
        var ours = new bool[256];
        int[] list = [0x14, 0x15, 0x17, 0x2F, 0x8D, 0xBF, 0xE0];
        foreach (var op in list)
            ours[op] = true;
        for (var op = 0x00; op <= 0x0F; op++) ours[op] = true;
        for (var op = 0x9A; op <= 0x9F; op++) ours[op] = true;
        for (var op = 0xAC; op <= 0xAF; op++) ours[op] = true;
        for (var op = 0xC1; op <= 0xC7; op++) ours[op] = true;
        for (var op = 0xCC; op <= 0xCF; op++) ours[op] = true;
        for (var op = 0xE6; op <= 0xEF; op++) ours[op] = true;
        return ours;
    }

    internal static int Next(byte* code, int pc) => pc + 1 + Lengths[code[pc]];

    private int Var(int id) => get(id);

    /// <summary>The instruction at <paramref name="pc"/> run if it's ReMixed's: the offset to go on from; −1 for the
    /// remaster's own.</summary>
    public int Step(byte* code, int pc, int size)
    {
        var op = code[pc];
        if (!Ours[op])
            return -1;
        var p = code + pc + 1;
        var next = Next(code, pc);
        switch (op)
        {
            case 0x00: // NOT
                set(0, Var(0) == 0 ? 1 : 0);
                return next;
            case 0x01: // IF var 0
                return Var(0) != 0 ? next : Skip(code, pc, size, 0x03020401, 0);
            case 0x02 or 0x03: // ELSE IF, ELSE reached from a taken branch: on past the ENDIF
                return SkipToClose(code, pc, size, 0x0401);
            case 0x05: // SWITCH var
                return Skip(code, pc, size, 0x07060509, Var(p[0]));
            case 0x08: // BREAK
                return SkipToClose(code, pc, size, 0x0905);
            case 0x04 or 0x06 or 0x07 or 0x09 or 0x0E: // ENDIF, CASE, DEFAULT, ENDSWITCH, LABEL
                return next;
            case 0x0A: // FUNCTION: its body is passed over, to after the next RETURN
                return Find(code, pc, size, 0x0B, -1) ?? next;
            case 0x0B: // RETURN
                if (_depth == 0)
                {
                    Note("a RETURN with nothing called");
                    return next;
                }
                return _calls[--_depth];
            case 0x0C: // CALL function p0
            {
                var target = Find(code, Start, size, 0x0A, p[0]);
                if (target == null)
                {
                    Note($"CALL 0x{p[0]:X2}: no such function");
                    return next;
                }
                if (_depth < _calls.Length)
                    _calls[_depth++] = (ushort)next;
                return target.Value;
            }
            case 0x0D: // GOTO label p0
                return Find(code, Start, size, 0x0E, p[0]) ?? Missing($"GOTO 0x{p[0]:X2}: no such label", next);
            case 0x0F: // with p1 = 3: drop p2 calls, then GOTO p0
                if (p[1] != 3)
                    return next;
                _depth = Math.Max(0, _depth - p[2]);
                return Find(code, Start, size, 0x0E, p[0]) ?? Missing($"0x0F's GOTO 0x{p[0]:X2}: no such label", next);
            case 0xAC or 0xAD or 0xAE or 0xAF: // SETHI, SETHI_VAR, SET, SET_VAR
            {
                var value = p[2] | p[3] << 8;
                if ((op & 1) != 0)
                    value = Var(value);
                set(p[0] | p[1] << 8, op < 0xAE ? value << 16 : value);
                return next;
            }
            case 0xBF: // RANDOM: var = rand() % n
            {
                var n = p[1] | p[2] << 8;
                var roll = Random.Shared.Next(0x8000);
                set(p[0] & 0x7F, n == 0 ? roll : roll % n);
                return next;
            }
            case 0xC1 or 0xC4: // PEEK, POKE: size bytes at [base var] + offset
            {
                var at = Var(p[3] & 0x7F);
                if (at == -1)
                    return next;
                var address = (uint)(at + (p[1] | p[2] << 8));
                var length = p[4] == 0 ? 1 : p[4] > 4 ? 4 : p[4];
                if (op == 0xC1)
                    set(p[0], memory.Read(address, length));
                else
                    memory.Write(address, length, Var(p[0] & 0x7F));
                return next;
            }
            case 0xC2: // UNIT_ADDRESS: the battle unit with that event id, else that sprite set
                set(p[1] & 0x7F, (int)memory.FindUnit(p[0]));
                return next;
            case 0xC3: // ROSTER_ADDRESS: the roster slot with that sprite set or name id
                set(p[1] & 0x7F, (int)memory.FindRoster(p[0]));
                return next;
            case 0x14: // MUSIC: vanilla's CallFunction 0xC (the PS1's 0x8013D278 after 0x80165FD4–D8) with track p0
                if (PlayMusic != null)
                    PlayMusic(p[0]);
                else
                    Note("0x14: no music player");
                return next;
            case 0x8D: // ITEMS (BATTLE.BIN 0x80158E4C): p0's high nibble what's counted, its low the operation; p1 the item
                return ItemCount(p[0] >> 4, p[0] & 0xF, p[1], p[2], next);
            case 0x9A: // LIST_RESET: the list's index (byte 0x8015029A) = 0
                memory.Write(Ps1Memory.ListIndex, 1, 0);
                return next;
            case 0x9B or 0x9C or 0x9D or 0x9E: // LIST: a value (0x9B, 0x9D: u16) or a variable (0x9C, 0x9E) into ReMixed's
            {                                  // words, at the next index (0x9B, 0x9C; it goes on, wrapping at 0x80) or at p0
                var given = ((op - 0x9B) & 2) != 0;
                var raw = given ? p[1] | p[2] << 8 : p[0] | p[1] << 8;
                var value = ((op - 0x9B) & 1) != 0 ? Var(raw & 0xFF) : raw & 0xFFFF;
                int index;
                if (given)
                    index = p[0];
                else
                {
                    index = memory.Read(Ps1Memory.ListIndex, 1);
                    memory.Write(Ps1Memory.ListIndex, 1, (index + 1) & 0x7F);
                }
                memory.Write(Ps1Memory.ExtraWords + 4 * (uint)index, 4, value);
                return next;
            }
            case 0xCD: // MAP_SETTINGS: the current event's (var 0x27) word in the per-event table = (p2 << 10) + (p0 << 8 | p1)
                memory.Write(Ps1Memory.MapSettings + 2 * (uint)Var(0x27), 2, (p[2] << 10) + (p[0] << 8 | p[1]));
                return next;
            case 0xC7: // HAS_CHARACTER: var 0 = whether the roster (flag 0x80) has the character
                set(0, memory.HasCharacter(p[0], p[1] | p[2] << 8) ? 1 : 0);
                return next;
            case 0xE6: // PATCH_NEXT: the next instruction (past other 0xE6s) gets var's bytes at its parameter offset
            {
                var target = pc + 3;
                while (target < size && code[target] == 0xE6)
                    target += 3;
                var value = Var(p[1]);
                var at = target + 1 + (p[0] & 0x3F);
                for (var k = 0; k <= p[0] >> 6 && at + k < size; k++)
                    code[at + k] = (byte)(value >> (8 * k));
                return next;
            }
            case 0xCE: // CONDITIONS: event n's battle event conditions into the event's tables (BATTLE.BIN 0x801595F4)
            {
                var n = p[0] | p[1] << 8;
                if (Presets == null || (n + 1) * PresetBytes > Presets.Length)
                    return Missing($"0xCE {n:X}: no preset", next);
                for (var k = 0; k < PresetOffsets + PresetRecords; k++)
                    memory.Write(k < PresetOffsets ? Ps1Memory.ConditionSets + (uint)k : Ps1Memory.Conditions + (uint)(k - PresetOffsets),
                                 1, Presets[n * PresetBytes + k]);
                return next;
            }
            case 0xCF: // NATIVE_CALL: ReMixed's MIPS code at the address
            {
                var address = (uint)(p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24);
                if (Natives.TryGetValue(address, out var native))
                    native();
                else if (_passed.Add((int)address))
                    note($"the native routine 0x{address:X8} isn't ported yet; passed over");
                return next;
            }
            case 0xE7: // UNIT_ON_TILE: var 0 = the battle unit at x, y, level
                set(0, (int)memory.UnitOnTile(p[0], p[1], p[2]));
                return next;
            case 0x2F: // LOOP (BATTLE.BIN 0x80159868, in place of vanilla's in the wait dispatcher)
                return Loop(code, pc, p, next);
            case 0xE8 or 0xE9: // TILE (BATTLE.BIN 0x80158BF0): var p0 = byte p1 of the map tile at (p2, p3, level p4), or
            {                  // 0xE9 the byte = var p0; the PS1's tiles (0x8018F8CC, 8 bytes each) laid out as the remaster's
                var tile = TileAt == null ? 0 : TileAt(p[2], p[3], p[4]);
                if (op == 0xE8)
                    set(p[0], tile == 0 ? 0 : ((byte*)tile)[p[1] & 7]);
                else if (tile != 0)
                    ((byte*)tile)[p[1] & 7] = (byte)get(p[0]);
                return next;
            }
            case 0xE0: // CREATE_UNIT: a battle unit made in slot p3 (CreateUnit)
                note(MakeUnit != null ? MakeUnit((nint)p) : "0xE0: no unit making; passed over");
                return next;
            default:
                if (_passed.Add(op))
                    note($"opcode 0x{op:X2} ({string.Join(" ", Enumerable.Range(0, Lengths[op]).Select(k => p[k].ToString("X2")))}) isn't ported yet; passed over");
                return next;
        }
    }

    internal static bool Ported(int op) => Done.Contains(op);

    /// <summary>The variables whose 0x2F loop is running (the PS1's bits at 0x80152AE0, one per variable).</summary>
    private readonly HashSet<int> _looping = [];

    /// <summary>
    /// ReMixed's 0x2F p0 var offset (a halfword): while var isn't 0, back to the start of the parallel block it's in
    /// (offset 0: the instruction after the block's 0x2A, where its task started), or by the offset (back from the 0x2F,
    /// or on from after it); then on past it. With p0, the variable counts down a step each time round (from p0 − 1 when
    /// the loop starts, for p0 of 2 or more). Its events loop a unit's gesture while a scene goes on (Luso's goblins,
    /// 0x0EA: walk, animation, wait, until the scene clears var 0x70).
    /// </summary>
    private int Loop(byte* code, int pc, byte* p, int next)
    {
        int variable = p[1], value = get(variable);
        var running = _looping.Contains(variable);
        if (running && value == 0)
        {
            _looping.Remove(variable);
            return next;
        }
        if (!running && p[0] >= 2)
            value = p[0] - 1;
        if (value == 0)
            return next;
        _looping.Add(variable);
        if (p[0] != 0)
            value--;
        set(variable, value);
        var offset = (short)(p[2] | p[3] << 8);
        return offset < 0 ? pc + offset : offset > 0 ? next + offset : BlockStart(code, pc, next);
    }

    /// <summary>Where the parallel block holding <paramref name="pc"/> starts (after its 0x2A; 0x2B ends one), or
    /// <paramref name="next"/> outside any.</summary>
    private static int BlockStart(byte* code, int pc, int next)
    {
        var open = new Stack<int>();
        for (var at = Start; at < pc; at = Next(code, at))
        {
            if (code[at] == 0x2A)
                open.Push(Next(code, at));
            else if (code[at] == 0x2B && open.Count > 0)
                open.Pop();
        }
        return open.Count > 0 ? open.Peek() : next;
    }

    /// <summary>
    /// ReMixed's 0x8D: kind 3 is the Poachers' Den's stock (0x80059494, up to 255; no event uses it), any other the
    /// inventory (0x800596E0, up to 99 with the equipped ones), kind 2 counting the equipped ones too. Operations 0–4
    /// compare that count with <paramref name="value"/> into var 0 (=, ≤, ≥, &lt;, &gt;); 6 and 7 add and take away
    /// the value, 9, A and B set, add and take away var value; 8 puts the count in var value. Adding stops at the cap,
    /// counting the equipped ones; taking away stops at 0.
    /// </summary>
    private int ItemCount(int kind, int operation, int item, int value, int next)
    {
        if (Items is not { } items || kind == 3)
            return Missing($"0x8D kind {kind} (the {(kind == 3 ? "Poachers' Den" : "inventory")}) isn't ported", next);
        var owned = items.Owned(item);
        var total = items.Total(item);
        var count = kind == 2 ? total : owned;
        void Put(int newOwned, int newTotal) => items.SetOwned(item, (byte)(newOwned - Math.Max(0, newTotal - 99)));
        switch (operation)
        {
            case < 5:
                set(0, operation switch
                {
                    0 => count == value,
                    1 => count <= value,
                    2 => count >= value,
                    3 => count < value,
                    _ => count > value,
                } ? 1 : 0);
                break;
            case 6: Put(owned + value, total + value); break;
            case 0xA: Put(owned + Var(value), total + Var(value)); break;
            case 7: items.SetOwned(item, Math.Max(0, owned - value)); break;
            case 0xB: items.SetOwned(item, Math.Max(0, owned - Var(value))); break;
            case 8: set(value, count); break;
            case 9: Put(Var(value), Var(value) + total - owned); break;
        }
        return next;
    }

    private int Missing(string what, int next)
    {
        Note(what);
        return next;
    }

    private void Note(string what) => note(what);

    /// <summary>ReMixed's 0x801587B8: from <paramref name="from"/> on, the first instruction <paramref name="op"/>
    /// (with parameter <paramref name="param"/>, or any for −1); the offset after it.</summary>
    private static int? Find(byte* code, int from, int size, int op, int param)
    {
        for (var at = from; at < size; at = Next(code, at))
        {
            if (code[at] == op && (param < 0 || code[at + 1] == param))
                return Next(code, at);
        }
        return null;
    }

    /// <summary>ReMixed's 0x8015880C: the first instruction after <paramref name="pc"/> whose opcode is one of the four
    /// bytes of <paramref name="set"/>; stops at END (0xDB) on the instruction before it.</summary>
    private static (int At, int Op) Scan(byte* code, int pc, int size, uint set)
    {
        var at = pc;
        while (true)
        {
            var next = Next(code, at);
            if (next >= size || code[next] == 0xDB)
                return (at, -1);
            at = next;
            for (var k = 0; k < 4; k++)
            {
                if (code[at] == (byte)(set >> (8 * k)))
                    return (at, code[at]);
            }
        }
    }

    /// <summary>A false IF or a SWITCH (0x80158928): skip to the branch that's taken. <paramref name="set"/> is what
    /// closes, opens or picks a branch; nested IFs and SWITCHes are passed over.</summary>
    private int Skip(byte* code, int pc, int size, uint set, int value)
    {
        var depth = 0;
        var looking = set;
        var at = pc;
        while (true)
        {
            var (found, op) = Scan(code, at, size, looking);
            if (op < 0)
                return found == at ? Next(code, at) : Next(code, found); // END: go on to it
            at = found;
            var next = Next(code, at);
            switch (op)
            {
                case 0x01:
                    looking = 0xFFFF0401;
                    depth++;
                    continue;
                case 0x05:
                    looking = 0xFFFF0905;
                    depth++;
                    continue;
                case 0x04 or 0x09:
                    if (depth == 0)
                        return next;
                    depth--;
                    looking = depth == 0 ? set : set & 0xFFFF;
                    continue;
                case 0x02: // ELSE IF var
                    if (Var(code[at + 1]) != 0)
                        return next;
                    looking = set;
                    continue;
                case 0x06: // CASE
                    if ((code[at + 1] | code[at + 2] << 8) != value)
                    {
                        looking = set;
                        continue;
                    }
                    return next;
                default: // ELSE, DEFAULT (and a 0x00 the scan's zero bytes match)
                    return next;
            }
        }
    }

    /// <summary>ELSE or BREAK reached (0x80158A08): on past the closing ENDIF or ENDSWITCH, nested ones counted.</summary>
    private static int SkipToClose(byte* code, int pc, int size, uint set)
    {
        var depth = 0;
        var at = pc;
        while (true)
        {
            var (found, op) = Scan(code, at, size, set);
            if (op < 0)
                return found == at ? Next(code, at) : Next(code, found);
            at = found;
            if (op is 0x01 or 0x05)
                depth += 2;
            if (depth == 0)
                return Next(code, at);
            depth--;
        }
    }
}
