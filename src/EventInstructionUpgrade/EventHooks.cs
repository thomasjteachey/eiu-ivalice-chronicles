using System.Runtime.InteropServices;

namespace EventInstructionUpgrade;

/// <summary>
/// The Event Instruction Upgrade's events run in the remaster (docs/INSTRUCTIONS.md, docs/INTERNALS.md). A mod ships an
/// upgrade event as `script/enhanced/eventNNN.e` with <see cref="Magic"/> in front (two LABELs no upgrade event uses),
/// and two hooks act only while the code the remaster runs ([0xD40A90] + 4) starts with it:
/// <list type="bullet">
/// <item>the instruction fetch (0x23AFD8, in the event runner 0x23A868; r11d the offset) hands each instruction to
/// <see cref="EventScript.Step"/>, which runs the upgrade's own and says where to go on (the runner's "next" path,
/// 0x23B63A, takes the offset from [rsp+0x44]); the remaster runs the rest;</item>
/// <item>the length helper (0x2CBE2C, rcx the instruction) gives the upgrade's lengths for that code, so the
/// remaster's own scans (labels, loops) read it right.</item>
/// </list>
/// A third hook, on the variables' executor, keeps the upgrade's words (variables 0xB00–0xB7F) for every event.
/// </summary>
internal static unsafe class EventHooks
{
    /// <summary>LABEL 0x2C, LABEL 0x3C: what an upgrade event's code starts with.</summary>
    internal static readonly byte[] Magic = [0x0E, 0x2C, 0x0E, 0x3C];
    internal static uint MagicWord => BitConverter.ToUInt32(Magic);
    internal const int MaxCode = 0x2000;
    /// <summary>The buffer the code pointer points to: a u32 (the remaster puts the script's size there), then the code
    /// from offset 4 (0x2340A4 copies it to 0xD3CD54), where the remaster starts, as the PS1 does.</summary>
    internal const int Size = EventScript.Start + MaxCode;

    internal const string FetchPattern = "48 8B 05 ?? ?? ?? ?? 41 8B DA 49 63 F3 45 33 D2 89 5C 24 54 48 89 75 C0";
    internal const string NextPattern = "45 84 C0 0F 85 ?? ?? ?? ?? 44 8B 5C 24 44 E9";
    internal const string LengthPattern = "80 39 E6 75 09 0F B6 41 01 83 C0 02 EB 10 0F B6 01 48 8D 15";
    private const int FetchMoved = 16, LengthMoved = 14, LengthDone = 0x1E;

    private static Log _log = null!;
    private static EventScript _script = null!;
    private static byte* _table;
    private static byte* _running; // the code the script state belongs to
    /// <summary>The running upgrade event's code, for a harness (its offsets are the built event file's).</summary>
    internal static nint RunningCode => (nint)_running;
    private static int _steps;
    /// <summary>The last instruction of an upgrade event fetched (its offset and opcode), for a test harness.</summary>
    internal static volatile int LastPc = -1, LastOp = -1;
    /// <summary>Log every instruction fetched (a test harness's "trace on").</summary>
    internal static volatile bool TraceSteps;
    /// <summary>Where the hooks went (RVAs), for the offline check.</summary>
    internal static (nint Fetch, nint Next, nint Length, nint Variables) Places;
    /// <summary>Whether the hooks are in.</summary>
    internal static bool Installed { get; private set; }

    /// <summary>What a mod built on the upgrade adds (<see cref="Api"/>).</summary>
    internal static readonly List<Action<nint>> StepObservers = [];
    internal static readonly List<(Func<int, bool> Holds, Func<int, int> Get, Action<int, int> Set)> ExtraVariables = [];
    internal static readonly List<Action<uint>> GraphicListeners = [];
    internal static readonly List<Action<string>> NoteListeners = [], ErrorListeners = [];
    /// <summary>The event's PS1 memory and its interpreter, once installed.</summary>
    internal static Ps1Memory? Memory;
    internal static EventScript? Script => _script;

    public static void Install(Game game)
    {
        _log = game.Log;
        var image = game.Image;
        var fetch = image.FindUnique(FetchPattern);
        var code = (byte**)GameImage.Rip(fetch, 3, 7);
        var next = image.FindUnique(NextPattern) + 9;
        var length = image.FindUnique(LengthPattern);
        if (next < fetch - 0x1000 || next > fetch + 0x1000)
            throw new InvalidDataException("the event runner isn't laid out as this DLL expects");
        var (get, set) = EventVariables.Find(image);
        GetVariable = (delegate* unmanaged<int, int>)get;
        SetVariable = (delegate* unmanaged<int, int, int, void>)set;
        _variables = EventVariables.Words(set);
        _hooks = game.Hooks;
        InstallWords(image);
        _table = (byte*)NativeMemory.Alloc(256);
        EventScript.Lengths.CopyTo(new Span<byte>(_table, 256));
        _script = NewScript(game);
        Places = (fetch - image.ImageStart, next - image.ImageStart, length - image.ImageStart, (nint)_variables - image.ImageStart);

        var stub = GameImage.AllocateCodeNear(fetch, 512);
        var bytes = FetchStub(stub, code, (nint)(delegate* unmanaged<byte*, int, int>)&Step, image.ImageStart, next,
                              fetch + FetchMoved);
        bytes.CopyTo(new Span<byte>(stub, bytes.Length));
        GameImage.WriteCode(fetch, Jump(fetch, (nint)stub, FetchMoved));

        var lengthStub = GameImage.AllocateCodeNear(length, 256);
        var lengthBytes = LengthStub(code, _table, length + LengthMoved, length + LengthDone);
        lengthBytes.CopyTo(new Span<byte>(lengthStub, lengthBytes.Length));
        GameImage.WriteCode(length, Jump(length, (nint)lengthStub, LengthMoved));
        Installed = true;
        _log.Info($"the instruction fetch at 0x{fetch - image.ImageStart:X}, the length helper at "
                  + $"0x{length - image.ImageStart:X}; {Enumerable.Range(0, 256).Count(op => EventScript.Ours[op] && EventScript.Ported(op))} "
                  + "of the upgrade's opcodes run");
    }

    /// <summary>The game's event variable getter and setter (<see cref="EventVariables"/>); the offline check swaps them.</summary>
    internal static delegate* unmanaged<int, int> GetVariable;
    internal static delegate* unmanaged<int, int, int, void> SetVariable;

    /// <summary>
    /// The remaster's event variables: [0x7830D8] points to the 32-bit words (var 0 at +0, var 1 at +4, as its
    /// comparisons read them; the PS1's 0x8005771C). The upgrade's word variables (ids under 0x80) are read and written
    /// there: the getter and setter (0x2332D8, 0x233368) use var 0 as scratch and put it back, so var 0, the one
    /// the upgrade's If tests, would always read 0 and never change, and the setter cuts a negative value (any PS1
    /// address) to its low byte. The flags (0x80 on) go through them.
    /// </summary>
    private static int** _variables;

    /// <summary>
    /// The upgrade's words: its variables 0xB00–0xB7F and the text values its 0x9B–0x9E push are words at PS1
    /// 0x80165EF4 (0x100 of them reachable), over vanilla data in BATTLE.BIN's memory. The remaster's variables stop at
    /// 0x3FF: from 0x400 its address helper (0x242984) returns nothing and the executor (0x242700, every variable read
    /// and write: the getter, the setter, the arithmetic 0xB0–0xBE and the comparisons) ends the running event
    /// (0x2F7EC8). A hook on the executor does what it would for those ids in the mod's store, which starts as the
    /// PS1's file has it (<c>eiu-battle-words.bin</c>, written by the build) and isn't reset: BATTLE.BIN stays loaded
    /// through a chain of scenes and battles, and when the PS1 reloads it isn't known yet.
    /// </summary>
    internal static int* Words;
    internal const int WordsFirst = 0xB00, WordsCount = 0x80;
    internal const string WordsFileName = "eiu-battle-words.bin";
    /// <summary>The battle event variables' executor (0x242700): (op, dest, source, r9, flag), op 0xB0–0xBE, an odd op's
    /// source a variable. The world's copy (0x2EC038, its own variables) isn't hooked. The offline check swaps it.</summary>
    internal static delegate* unmanaged<uint, uint, uint, nint, nint, void> GameArithmetic;
    internal static nint ArithmeticAt;
    internal const string ArithmeticPattern =
        "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 54 41 55 41 56 41 57 48 83 EC 20 33 FF 41 8B D8 8B F2 8B E9 " +
        "41 BD 60 03 00 00 44 8D 7F 0F 44 8D 67 01 81 F9 BE 00 00 00";

    private static IHooks _hooks = null!;

    private static void InstallWords(GameImage image)
    {
        Words = (int*)NativeMemory.AllocZeroed((nuint)Ps1Memory.ExtraWordsBytes);
        var file = Path.Combine(Path.GetDirectoryName(typeof(EventHooks).Assembly.Location) ?? "", WordsFileName);
        if (File.Exists(file))
        {
            var bytes = File.ReadAllBytes(file);
            bytes.AsSpan(0, Math.Min(bytes.Length, Ps1Memory.ExtraWordsBytes)).CopyTo(new Span<byte>(Words, Ps1Memory.ExtraWordsBytes));
        }
        else
            _log.Info($"no {WordsFileName} beside the DLL; the upgrade's words start at 0");
        // The battle's copy is the one whose variable helper (called at +0x65) reads the battle variables' pointer.
        nint found = 0;
        foreach (var at in image.FindExactly(ArithmeticPattern, 2))
        {
            var helper = *(byte*)(at + 0x65) == 0xE8 ? GameImage.Rip(at + 0x65, 1, 5) : 0;
            if (helper != 0 && *(byte*)(helper + 0x10) == 0x48 && GameImage.Rip(helper + 0x10, 3, 7) == (nint)_variables)
                found = at;
        }
        if (found == 0)
            throw new InvalidDataException("the battle event variables' executor isn't the code this DLL expects");
        GameArithmetic = (delegate* unmanaged<uint, uint, uint, nint, nint, void>)_hooks.Hook(
            found, (nint)(delegate* unmanaged<uint, uint, uint, nint, nint, void>)&Arithmetic, "the event variables' executor");
        ArithmeticAt = found;
    }

    internal static bool IsWord(uint id) => id - WordsFirst < WordsCount;

    [UnmanagedCallersOnly]
    private static void Arithmetic(uint op, uint dest, uint source, nint r9, nint flag)
    {
        var sourceIsWord = op != 0xBE && (op & 1) != 0 && IsWord(source);
        if (!IsWord(dest) && !sourceIsWord)
        {
            GameArithmetic(op, dest, source, r9, flag);
            return;
        }
        try
        {
            var value = op == 0xBE ? 0 : (op & 1) == 0 ? (int)source : sourceIsWord ? Words[source - WordsFirst] : GetWord((int)source);
            if (IsWord(dest))
                Words[dest - WordsFirst] = Apply(op, Words[dest - WordsFirst], value);
            else
                GameArithmetic(op & ~1u, dest, (uint)value, r9, flag); // the value as a literal: the remaster does the rest
        }
        catch (Exception e)
        {
            Note($"the variable executor failed: {e.Message}");
        }
    }

    /// <summary>The executor's arithmetic (0x242700): ZERO, ADD, SUB, MUL, DIV, MOD, AND, OR by op pair; a division by 0
    /// leaves the variable (the remaster ends the event there).</summary>
    internal static int Apply(uint op, int current, int value) => op switch
    {
        0xBE => 0,
        < 0xB2 => current + value,
        < 0xB4 => current - value,
        < 0xB6 => current * value,
        < 0xB8 => value == 0 ? current : current / value,
        < 0xBA => value == 0 ? current : current % value,
        < 0xBC => current & value,
        < 0xBE => current | value,
        _ => current,
    };

    /// <summary>An event variable as the events see it: a word (under 0x80) straight from the game's array, one a mod
    /// keeps itself (<see cref="ExtraVariables"/>), else through the game's getter.</summary>
    internal static int GetWord(int id)
    {
        if (id < 0x80)
            return Word(id);
        foreach (var (holds, get, _) in ExtraVariables)
            if (holds(id))
                return get(id);
        return GetVariable == null ? 0 : GetVariable(id);
    }

    /// <summary>An event variable's word (an id below 0x80) straight from the game's array, or 0 while it has none (as in
    /// the offline check, where the game's own getter can't run).</summary>
    internal static int Word(int id) => _variables != null && *_variables != null && id is >= 0 and < 0x80 ? (*_variables)[id] : 0;

    internal static void SetWord(int id, int value)
    {
        if (id < 0x80)
        {
            if (_variables != null && *_variables != null)
                (*_variables)[id] = value;
            return;
        }
        foreach (var (holds, _, set) in ExtraVariables)
            if (holds(id))
            {
                set(id, value);
                return;
            }
        if (SetVariable != null)
            SetVariable(id, value, 0);
    }

    /// <summary>The event runner's CallFunction 0xC (0x23C38D): [0xD40900] = 0, [0xD4091C] = 0x13, [0xD408FC] = 0, then
    /// 0x234990(0x13, 0), which queues the track on the sound channel (the PS1's 0x80165FD8, FD4, FD6 and 0x8013D278).
    /// The upgrade's PlayTrack (0x14) is the same with its own track: FD4 = 0, FD6 = the track, FD8 = 0.</summary>
    internal const string MusicPattern =
        "41 83 FF 0C 75 24 41 8D 47 07 66 44 89 2D ?? ?? ?? ?? 8B C8 66 89 05 ?? ?? ?? ?? 33 D2 66 44 89 2D ?? ?? ?? ?? E8";

    internal static (nint Fd4, nint Fd6, nint Fd8, nint Play) Music(GameImage image)
    {
        var at = image.FindUnique(MusicPattern);
        return (GameImage.Rip(at + 20, 3, 7), GameImage.Rip(at + 29, 4, 8), GameImage.Rip(at + 10, 4, 8), GameImage.Rip(at + 37, 1, 5));
    }

    /// <summary>The battle-mode loop's buttons (0x37FB8E): mov edx, [held]; mov [before], edx; then the pad's bytes; and
    /// mov [pressed], edx 0x5C on. The world map's copy (0x2CC98E, its own words) tests a byte where this loads the
    /// mode's flags (8B 05).</summary>
    internal const string ButtonsPattern =
        "8B 15 ?? ?? ?? ?? 89 15 ?? ?? ?? ?? 44 0F B6 40 02 0F B6 40 03 41 C1 E0 08 44 0B C0 0F B6 41 02 41 C1 E0 08 44 0B C0 0F B6 41 03 41 C1 E0 08 44 0B C0 8B 05";

    internal static (nint Held, nint Before, nint Pressed) ButtonWords(GameImage image)
    {
        var at = image.FindUnique(ButtonsPattern);
        if (*(byte*)(at + 0x5C) != 0x89 || *(byte*)(at + 0x5D) != 0x15)
            throw new InvalidDataException("the buttons aren't kept as this DLL expects");
        return (GameImage.Rip(at, 2, 6), GameImage.Rip(at + 6, 2, 6), GameImage.Rip(at + 0x5C, 2, 6));
    }

    private static EventScript NewScript(Game game)
    {
        var levels = game.Image.FindUnique(EventTables.EnemyLevelsPattern);
        var settings = EventTables.MapSettings(game.Image);
        var (sets, records) = EventTables.ConditionTables(game.Image);
        // The PS1's button words (held, held before, pressed: 12 bytes from 0x80045944), made from the remaster's input
        // as an event reads them (Ps1Pad); the remaster's own copy (ButtonWords) is made only in a battle's frame.
        Ps1Pad.Install(game.Image);
        var memory = new Ps1Memory(game.TurnCode.Units, game.Units.Roster, Note)
            .Map(Ps1Memory.Variables, Ps1Memory.VariablesBytes, (byte*)*_variables)
            .Map(Ps1Memory.Buttons, 12, (byte*)Ps1Pad.Words)
            .Refreshed(Ps1Memory.Buttons, 12, Ps1Pad.Refresh)
            .Map(Ps1Memory.ConditionSets, Ps1Memory.ConditionSetsBytes, (byte*)sets)
            .Map(Ps1Memory.Conditions, Ps1Memory.ConditionsBytes, (byte*)records)
            .Map(0x80193844, 2, (byte*)GameImage.Rip(levels + 4, 3, 7))
            .Map(0x80193920, 1, (byte*)GameImage.Rip(levels + 11, 2, 6))
            .Map(Ps1Memory.MapSettings, Ps1Memory.MapSettingsBytes, (byte*)settings)
            .Map(Ps1Memory.ExtraWords, Ps1Memory.ExtraWordsBytes, (byte*)Words);
        memory.GraphicWritten = slot =>
        {
            foreach (var listener in GraphicListeners)
                listener(slot);
        };
        Memory = memory;
        var script = new EventScript(GetWord, SetWord, memory, Note);
        var (inventory, total) = Inventory.FindCount(game.Image);
        script.Items = (item => ((byte*)inventory)[item], (item, count) => ((byte*)inventory)[item] = (byte)count,
                        item => ((delegate* unmanaged<int, int>)total)(item));
        var (fd4, fd6, fd8, play) = Music(game.Image);
        script.PlayMusic = track =>
        {
            *(short*)fd4 = 0;
            *(short*)fd6 = (short)track;
            *(short*)fd8 = 0;
            ((delegate* unmanaged<int, int, void>)play)(track, 0);
        };
        var map = game.Map;
        script.TileAt = (x, y, level) => (nint)map.TileAt(x, y, level);
        try
        {
            if (!CreateUnit.Found)
                CreateUnit.Find(game.Image);
            var units = game.TurnCode.Units;
            var roster = game.Units.Roster;
            script.MakeUnit = p => CreateUnit.Make((byte*)p, memory, units, roster, GetWord, SetWord);
        }
        catch (Exception e)
        {
            _log.Error($"the remaster's unit making wasn't found ({e.Message}); GenerateUnit (0xE0) does nothing");
        }
        return script;
    }

    private static readonly HashSet<string> Traced = [];

    /// <summary>Logged once each (up to 128 lines, as <see cref="Tracer"/> does), and every time to the mods
    /// listening (<see cref="Api.OnMessage"/>).</summary>
    internal static void Note(string text)
    {
        bool first;
        lock (Traced)
            first = Traced.Count < 128 && Traced.Add(text);
        if (first)
            _log?.Info("trace " + text);
        foreach (var listener in NoteListeners)
            listener(text);
    }

    internal static void Error(string text)
    {
        _log?.Error(text);
        foreach (var listener in ErrorListeners)
            listener(text);
    }

    [UnmanagedCallersOnly]
    private static int Step(byte* code, int pc)
    {
        try
        {
            if (code != _running)
            {
                _running = code;
                _steps = 0;
                Note($"an upgrade event runs (its code at 0x{(nint)code:X})");
            }
            _steps++;
            LastPc = pc;
            LastOp = code[pc];
            if (TraceSteps)
                _log.Info($"step 0x{pc:X} op 0x{code[pc]:X2}");
            foreach (var observer in StepObservers)
                observer((nint)(code + pc));
            if (code[pc] == 0xD1)
                return PhaseBlock(code, pc, Size);
            return _script.Step(code, pc, Size);
        }
        catch (Exception e)
        {
            Error($"{e.Message} at offset 0x{pc:X}; passed over");
            return EventScript.Next(code, pc);
        }
    }

    private static readonly HashSet<int> MissingPhases = [];

    /// <summary>
    /// 0xD1 n: on to the instruction after the next 0xD2 n, as the remaster's own does (0x2425D0); ReMixed's battle events
    /// keep their phases in such blocks and write n in from var 1 (AlterParameter, 0xE6) as they start. With no such
    /// block before the END, the remaster's ends the task and goes back to offset 0, which ran an upgrade event from its
    /// start again and again (seen on the PC: the bombs battle's closing run, var 1 holding an address); here the event
    /// goes on to its END instead.
    /// </summary>
    internal static int PhaseBlock(byte* code, int pc, int size)
    {
        var phase = code[pc + 1];
        var at = EventScript.Next(code, pc);
        while (at < size && code[at] != 0xDB)
        {
            if (code[at] == 0xD2 && code[at + 1] == phase)
                return at + 2;
            at = EventScript.Next(code, at);
        }
        if (_log != null && MissingPhases.Add(phase))
            _log.Info($"no phase block 0x{phase:X2} after 0x{pc:X}; on to the END");
        return Math.Min(at, size - 1);
    }

    /// <summary>jmp rel32 to the stub, the rest of the moved bytes nops.</summary>
    private static byte[] Jump(nint from, nint to, int moved)
    {
        var jump = new byte[moved];
        Array.Fill(jump, (byte)0x90);
        jump[0] = 0xE9;
        BitConverter.TryWriteBytes(jump.AsSpan(1), checked((int)(to - (from + 5))));
        return jump;
    }

    /// <summary>
    /// The fetch's stub, entered by a jump with rsp 16-aligned, r11d the offset and r10d the last opcode: for code
    /// starting with <see cref="Magic"/>, <paramref name="step"/>(code, offset) runs the upgrade's instruction; its
    /// offset goes into [rsp+0x44] and on to <paramref name="next"/> with the registers that path expects (r14d 1, r13d
    /// 0, r12 the image base, r8b 0). Otherwise, and for −1, the moved code (mov rax, [code]; mov ebx, r10d; movsxd
    /// rsi, r11d; xor r10d, r10d) and back to <paramref name="resume"/>.
    /// </summary>
    internal static byte[] FetchStub(byte* stub, byte** code, nint step, nint imageBase, nint next, nint resume)
    {
        var c = new List<byte>();
        c.AddRange([0x50, 0x51, 0x52, 0x41, 0x50, 0x41, 0x51, 0x41, 0x52, 0x41, 0x53]); // push rax, rcx, rdx, r8–r11
        c.AddRange([0x48, 0xB8]); c.AddRange(BitConverter.GetBytes((ulong)code));      // mov rax, &code
        c.AddRange([0x48, 0x8B, 0x00]);                                                  // mov rax, [rax]
        c.AddRange([0x48, 0x85, 0xC0]);                                                  // test rax, rax
        var toTheirs1 = Branch(c, 0x84);                                                 // jz theirs
        c.AddRange([0x81, 0x78, 0x04]); c.AddRange(BitConverter.GetBytes(MagicWord));    // cmp dword [rax+4], magic
        var toTheirs2 = Branch(c, 0x85);                                                 // jne theirs
        c.AddRange([0x48, 0x81, 0xEC, 0x88, 0x00, 0x00, 0x00]);                          // sub rsp, 0x88
        Stub.SaveXmm(c, 0x20);
        c.AddRange([0x48, 0x89, 0xC1]);                                                  // mov rcx, rax
        c.AddRange([0x44, 0x89, 0xDA]);                                                  // mov edx, r11d
        c.AddRange([0x48, 0xB8]); c.AddRange(BitConverter.GetBytes((ulong)step));      // mov rax, step
        c.AddRange([0xFF, 0xD0]);                                                        // call rax
        Stub.RestoreXmm(c, 0x20);
        c.AddRange([0x48, 0x81, 0xC4, 0x88, 0x00, 0x00, 0x00]);                          // add rsp, 0x88
        c.AddRange([0x83, 0xF8, 0xFF]);                                                  // cmp eax, -1
        var toTheirs3 = Branch(c, 0x84);                                                 // je theirs
        c.AddRange([0x89, 0x44, 0x24, 0x38 + 0x44]);                                     // mov [rsp+0x38+0x44], eax
        Pops(c);
        c.AddRange([0x41, 0xBE, 0x01, 0x00, 0x00, 0x00]);                                // mov r14d, 1
        c.AddRange([0x45, 0x33, 0xED]);                                                  // xor r13d, r13d
        c.AddRange([0x49, 0xBC]); c.AddRange(BitConverter.GetBytes((ulong)imageBase));  // mov r12, image base
        c.AddRange([0x45, 0x33, 0xC0]);                                                  // xor r8d, r8d
        c.AddRange([0xC6, 0x44, 0x24, 0x40, 0x00]);                                      // mov byte [rsp+0x40], 0
        AbsoluteJump(c, next);
        var theirs = c.Count;
        foreach (var at in (int[])[toTheirs1, toTheirs2, toTheirs3])
            c.PutInt(at, theirs - (at + 4));
        Pops(c);
        c.AddRange([0x48, 0xB8]); c.AddRange(BitConverter.GetBytes((ulong)code));      // mov rax, &code
        c.AddRange([0x48, 0x8B, 0x00]);                                                  // mov rax, [rax]
        c.AddRange([0x41, 0x8B, 0xDA]);                                                  // mov ebx, r10d
        c.AddRange([0x49, 0x63, 0xF3]);                                                  // movsxd rsi, r11d
        c.AddRange([0x45, 0x33, 0xD2]);                                                  // xor r10d, r10d
        AbsoluteJump(c, resume);
        return [.. c];
    }

    /// <summary>
    /// The length helper's stub (entered by a jump from its start, rcx the instruction): inside code that starts with
    /// <see cref="Magic"/>, the upgrade's length from <paramref name="table"/> (+1) and return; otherwise the moved code
    /// (0xE6's length is its next byte + 2, then on to <paramref name="done"/>; the rest on to <paramref name="table0"/>).
    /// rcx and r8–r11 are kept.
    /// </summary>
    internal static byte[] LengthStub(byte** code, byte* table, nint table0, nint done)
    {
        var c = new List<byte>();
        c.AddRange([0x48, 0xB8]); c.AddRange(BitConverter.GetBytes((ulong)code));      // mov rax, &code
        c.AddRange([0x48, 0x8B, 0x00]);                                                  // mov rax, [rax]
        c.AddRange([0x48, 0x85, 0xC0]);                                                  // test rax, rax
        var a = Branch(c, 0x84);                                                         // jz theirs
        c.AddRange([0x81, 0x78, 0x04]); c.AddRange(BitConverter.GetBytes(MagicWord));    // cmp dword [rax+4], magic
        var b = Branch(c, 0x85);                                                         // jne theirs
        c.AddRange([0x48, 0x39, 0xC1]);                                                  // cmp rcx, rax
        var d = Branch(c, 0x82);                                                         // jb theirs
        c.AddRange([0x48, 0x8D, 0x90]); c.AddRange(BitConverter.GetBytes(Size));         // lea rdx, [rax+Size]
        c.AddRange([0x48, 0x39, 0xD1]);                                                  // cmp rcx, rdx
        var e = Branch(c, 0x83);                                                         // jae theirs
        c.AddRange([0x0F, 0xB6, 0x01]);                                                  // movzx eax, byte [rcx]
        c.AddRange([0x48, 0xBA]); c.AddRange(BitConverter.GetBytes((ulong)table));      // mov rdx, table
        c.AddRange([0x0F, 0xB6, 0x04, 0x02]);                                            // movzx eax, byte [rdx+rax]
        c.AddRange([0xFF, 0xC0, 0xC3]);                                                  // inc eax; ret
        var theirs = c.Count;
        foreach (var at in (int[])[a, b, d, e])
            c.PutInt(at, theirs - (at + 4));
        c.AddRange([0x80, 0x39, 0xE6]);                                                  // cmp byte [rcx], 0xE6
        c.AddRange([0x75, 0x15]);                                                        // jne +0x15 (past the next 21 bytes)
        c.AddRange([0x0F, 0xB6, 0x41, 0x01, 0x83, 0xC0, 0x02]);                          // movzx eax, [rcx+1]; add eax, 2
        AbsoluteJump(c, done);                                                           // (14 bytes)
        AbsoluteJump(c, table0);
        return [.. c];
    }

    /// <summary>A jcc rel32 (0F <paramref name="cc"/>) whose displacement is filled in later; its offset.</summary>
    private static int Branch(List<byte> c, byte cc)
    {
        c.AddRange([0x0F, cc, 0, 0, 0, 0]);
        return c.Count - 4;
    }

    private static void Pops(List<byte> c) =>
        c.AddRange([0x41, 0x5B, 0x41, 0x5A, 0x41, 0x59, 0x41, 0x58, 0x5A, 0x59, 0x58]); // pop r11 … rax

    private static void AbsoluteJump(List<byte> code, nint target)
    {
        code.AddRange([0xFF, 0x25, 0, 0, 0, 0]);
        code.AddRange(BitConverter.GetBytes((ulong)target));
    }
}
