using EventInstructionUpgrade;
using Reloaded.Hooks;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: EventInstructionUpgrade.Check <FFT_enhanced.exe> [live-hooks]");
    return 2;
}

var problems = 0;
var log = new Log(
    text => Console.WriteLine($"  {text}"),
    text => { problems++; Console.WriteLine($"  warning: {text}"); },
    text => { problems++; Console.WriteLine($"  error: {text}"); });
try
{
    var image = GameImage.FromFile(args[0]);
    var live = args.Length == 2 && args[1] == "live-hooks";
    IHooks hooks = live ? new LiveHooks(log) : new ReportedHooks(log);
    var game = new Game(image, log, hooks);
    EventHooks.Install(game);
    problems += SelfTest.PhaseBlocks(log);
    problems += SelfTest.GenerateUnit(game, log);
    if (live)
        problems += SelfTest.Scripts(game, log);
}
catch (Exception e)
{
    problems++;
    Console.WriteLine($"  error: {e}");
}
return problems == 0 ? 0 : 1;

/// <summary>Installs the real hooks on the mapped copy, then checks each entry now jumps away.</summary>
internal sealed unsafe class LiveHooks(Log log) : IHooks
{
    private readonly List<IHook<NativeAction>> _active = [];

    public nint Hook(nint function, nint detour, string name)
    {
        var hook = ReloadedHooks.Instance.CreateHook<NativeAction>((void*)detour, function).Activate();
        _active.Add(hook);
        var entry = (byte*)function;
        // jmp rel32, jmp [rip+disp32], or jmp [abs32] (Reloaded.Hooks' pointer in low memory)
        var jumps = entry[0] == 0xE9 || (entry[0] == 0xFF && (entry[1] == 0x25 || (entry[1] == 0x24 && entry[2] == 0x25)));
        if (!jumps)
            throw new InvalidOperationException($"hooking {name} didn't put a jump at its entry: " +
                string.Join(" ", Enumerable.Range(0, 16).Select(k => entry[k].ToString("X2"))));
        log.Info($"hooks: hooked {name} at 0x{function:X}; the original runs from 0x{hook.OriginalFunctionAddress:X}");
        return hook.OriginalFunctionAddress;
    }
}

[Function(CallingConventions.Microsoft)]
internal delegate void NativeAction();

/// <summary>The upgrade's pieces on the check's copy of the exe, with made-up units, variables and code.</summary>
internal static unsafe class SelfTest
{
    /// <summary>The hooks' places, the PS1 memory map, the length stub, a program through the interpreter, BlockLoop and
    /// the tile instructions, the word variables through the hooked executor, and names and JP written as the PS1 has them.</summary>
    public static int Scripts(Game game, Log log)
    {
        var failures = 0;
        void Check(string name, bool ok)
        {
            if (!ok) failures++;
            log.Info($"self-test: event instructions, {name}{(ok ? "" : ": WRONG")}");
        }
        var (fetch, next, length, variables) = EventHooks.Places;
        var places = $"fetch 0x{fetch:X}, next 0x{next:X}, lengths 0x{length:X}, variables 0x{variables:X}";
        Check($"the hooks' places: {places}", places == "fetch 0x23AFD8, next 0x23B63A, lengths 0x2CBE2C, variables 0x7830D8");
        // PS1 battle unit fields in the remaster's unit, as the job routine (SCUS 0x8005B6xx, 0x276C64) and the unit
        // setup (0x8005DFAC, 0x27xxxx) place them.
        var fields = string.Join(" ", new[] { 0x4B, 0x4E, 0x53, 0x58, 0x6D, 0x71, 0x15C, 0x15E, 0x162, 0x1B8, 0x1B9, 0x163, 0xF8 }
            .Select(o => $"{o:X}:{(Ps1Memory.UnitField(o) is var r and >= 0 ? r.ToString("X") : "-")}"));
        Check($"PS1 unit fields mapped: {fields}",
              fields == "4B:53 4E:57 53:5C 58:61 6D:76 71:7A 15C:18C 15E:18E 162:192 1B8:1EC 1B9:1ED 163:194 F8:10C");
        // The fields the unit build copies from the roster (SCUS 0x8005B1EC, the remaster's 0x276758): raw stats, jobs and
        // abilities, job levels, JP and total JP, each range's ends, and a gap.
        var copied = string.Join(" ", new[] { 0x72, 0x80, 0x81, 0x96, 0x99, 0xD1, 0xD2, 0xDB, 0xDC, 0x103, 0x104, 0x12B }
            .Select(o => $"{o:X}:{(Ps1Memory.UnitField(o) is var r and >= 0 ? r.ToString("X") : "-")}"));
        Check($"PS1 unit fields copied from the roster mapped: {copied}",
              copied == "72:7B 80:89 81:- 96:9F 99:A2 D1:DA D2:E4 DB:ED DC:F0 103:117 104:11E 12B:145");
        var musicAt = EventHooks.Music(game.Image);
        var musicPlaces = $"0x{musicAt.Fd4 - game.Image.ImageStart:X} 0x{musicAt.Fd6 - game.Image.ImageStart:X} "
                          + $"0x{musicAt.Fd8 - game.Image.ImageStart:X}, 0x{musicAt.Play - game.Image.ImageStart:X}";
        Check($"PlayTrack's music change: the PS1's 0x80165FD4, D6, D8 at {musicPlaces}", musicPlaces == "0xD4091C 0xD408FC 0xD40900, 0x234990");
        var buttonsAt = EventHooks.ButtonWords(game.Image);
        var buttons = $"0x{buttonsAt.Held - game.Image.ImageStart:X} 0x{buttonsAt.Before - game.Image.ImageStart:X} 0x{buttonsAt.Pressed - game.Image.ImageStart:X}";
        Check($"the buttons (PS1 0x80045944, 48, 4C): {buttons}", buttons == "0x1876EEC 0x187A3F4 0x1876EF0");
        var frameAt = game.Image.FindUnique(Ps1Pad.BattleFramePattern) - game.Image.ImageStart;
        var padAt = GameImage.Rip(game.Image.FindUnique(Ps1Pad.BattleFramePattern) + 11, 3, 7) - game.Image.ImageStart;
        var testAt = game.Image.FindUnique(Ps1Pad.ActionTestPattern) - game.Image.ImageStart;
        var (uiCheck, inputState) = Ps1Pad.Places;
        Check($"the PS1's buttons made from the remaster's input: the battle frame 0x{frameAt:X} (the first pad's record at " +
              $"[0x{padAt:X}]), the action test 0x{testAt:X} and from it the UI's action check " +
              $"0x{uiCheck - game.Image.ImageStart:X} on [0x{inputState - game.Image.ImageStart:X}], {Ps1Pad.Actions.Length} buttons",
              frameAt == 0x37FB7C && padAt == 0x2E942E8 && testAt == 0xFC598 && uiCheck - game.Image.ImageStart == 0xF0D00
              && inputState - game.Image.ImageStart == 0x3CD9EE8 && Ps1Pad.Actions.Length == 12);
        var (setsAt, recordsAt) = EventTables.ConditionTables(game.Image);
        Check($"LoadConditionals' tables: the condition sets at 0x{setsAt - game.Image.ImageStart:X}, the records at 0x{recordsAt - game.Image.ImageStart:X}",
              setsAt - game.Image.ImageStart == 0xD40040 && recordsAt - game.Image.ImageStart == 0xD3F550);
        var rosterFields = string.Join(" ", new[] { 0x0D, 0x0E, 0x14, 0x15, 0x16, 0x17, 0x19, 0x2B, 0x66, 0x67, 0x6D, 0x6E, 0x7A,
                                                     0x95, 0x96, 0xBD, 0xBE, 0xD0 }
            .Select(o => $"{o:X}:{(Ps1Memory.RosterField(o) is var r and >= 0 ? r.ToString("X") : "-")}"));
        var unitEquipment = $"{Ps1Memory.UnitField(0x1A):X} {Ps1Memory.UnitField(0x20):X} {Ps1Memory.UnitField(0x21):X}";
        Check($"PS1 roster fields mapped (the remaster's equipment is halfwords: +7 from EXP): {rosterFields}; a battle unit's equipment {unitEquipment}",
              rosterFields == "D:D E:E 14:1A 15:1C 16:1D 17:1E 19:20 2B:32 66:6D 67:- 6D:- 6E:80 7A:8C 95:A7 96:AE BD:D5 BE:- D0:11E"
              && unitEquipment == "1A 26 28");
        var settingsAt = game.Image.FindUnique(EventTables.MapSettingsPattern);
        Check($"the per-event map settings (PostEvent's table) at 0x{*(int*)(settingsAt + 21):X}", *(int*)(settingsAt + 21) == 0xD3F950);

        // The length stub: the upgrade's lengths inside marked code, the remaster's path elsewhere.
        var cell = (byte**)System.Runtime.InteropServices.NativeMemory.AllocZeroed(8);
        var table = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc(256);
        EventScript.Lengths.CopyTo(new Span<byte>(table, 256));
        var pads = GameImage.AllocateCode(32);
        byte[] padBytes = [0xC3, 0x90, 0x90, 0x90, 0xB8, 0x77, 0x00, 0x00, 0x00, 0xC3]; // done: ret; table0: mov eax, 0x77; ret
        padBytes.CopyTo(new Span<byte>(pads, padBytes.Length));
        var stub = GameImage.AllocateCode(256);
        var bytes = EventHooks.LengthStub(cell, table, (nint)(pads + 4), (nint)pads);
        bytes.CopyTo(new Span<byte>(stub, bytes.Length));
        var len = (delegate* unmanaged<byte*, int>)stub;
        var marked = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(EventHooks.Size + 16);
        *(int*)marked = 0x123; // the size the remaster puts there
        EventHooks.Magic.CopyTo(new Span<byte>(marked + 4, 4));
        marked[8] = 0xC2; marked[11] = 0xE6; marked[12] = 0x05; marked[14] = 0xE0;
        var other = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(16);
        other[0] = 0xE6; other[1] = 0x05; other[2] = 0xC2;
        *cell = marked;
        var lengths = $"{len(marked + 8)} {len(marked + 11)} {len(marked + 14)} | {len(other)} {len(other + 2)}";
        *cell = other;
        lengths += $" | {len(other)} {len(other + 2)}";
        *cell = null;
        lengths += $" | {len(other + 2)}";
        Check($"the length stub: the upgrade's 0xC2, 0xE6, 0xE0 in marked code, the remaster's elsewhere: {lengths}",
              lengths == "3 3 13 | 7 119 | 7 119 | 119");

        // The interpreter.
        byte[] program =
        [
            0x0E, 0x2C, 0x0E, 0x3C,
            0xCF, 0x00, 0x10, 0x15, 0x80,              // Routine 0x80151000 (given below)
            0xCF, 0x00, 0x06, 0x16, 0x80,              // Routine 0x80160600 (none: passed over, noted)
            0xAE, 0x00, 0x00, 0x05, 0x00,              // SET var 0 = 5
            0x01, 0xAE, 0x10, 0x00, 0x01, 0x00,        // If: var 0x10 = 1
            0x03, 0xAE, 0x10, 0x00, 0x02, 0x00, 0x04,  // Else: var 0x10 = 2; EndIf
            0xBE, 0x00, 0x00,                          // ZERO var 0 (the remaster's)
            0x01, 0x01, 0xAE, 0x11, 0x00, 0x09, 0x00, 0x04, 0xAE, 0x11, 0x00, 0x08, 0x00, // If { If { 9 } 8 }
            0x03, 0xAE, 0x11, 0x00, 0x03, 0x00, 0x04,  // Else { var 0x11 = 3 }
            0xAE, 0x05, 0x00, 0x07, 0x00,              // SET var 5 = 7
            0x05, 0x05,                                // Switch var 5
            0x06, 0x01, 0x00, 0xAE, 0x12, 0x00, 0x01, 0x00, 0x08,
            0x06, 0x07, 0x00, 0xAE, 0x12, 0x00, 0x07, 0x00, 0x08,
            0x07, 0xAE, 0x12, 0x00, 0x09, 0x00, 0x09,  // Default; EndSwitch
            0x0C, 0x20,                                // CallSub 0x20
            0x0D, 0x30,                                // GoTo 0x30
            0xAE, 0x13, 0x00, 0x63, 0x00,              // (passed over)
            0x0A, 0x20, 0xAE, 0x14, 0x00, 0x04, 0x00, 0x0B, // DefineSub 0x20 { var 0x14 = 4 } EndSub
            0x0E, 0x30,                                // GoToTarget 0x30
            0xC2, 0x85, 0x70,                          // UnitAddress: var 0x70 = the unit with event id 0x85
            0xAE, 0x71, 0x00, 0x2A, 0x00,              // var 0x71 = 0x2A
            0xC4, 0x71, 0x47, 0x00, 0x70, 0x01,        // SaveAddress its X (PS1 +0x47)
            0xC1, 0x15, 0x24, 0x00, 0x70, 0x01,        // LoadAddress its Brave (PS1 +0x24)
            0xAE, 0x72, 0x00, 0x34, 0x12,              // var 0x72 = 0x1234
            0xE6, 0x42, 0x72,                          // AlterParameter: 2 bytes at parameter 2
            0xAE, 0x16, 0x00, 0x00, 0x00,              // becomes var 0x16 = 0x1234
            0xC1, 0x17, 0x40, 0x01, 0x70, 0x01,        // LoadAddress PS1 +0x140: not mapped
            0xAC, 0x18, 0x00, 0x01, 0x00,              // LUI var 0x18 = 0x10000
            0xAC, 0x1A, 0x00, 0x19, 0x80,              // var 0x1A = 0x80190000
            0xC4, 0x72, 0x44, 0x38, 0x1A, 0x02,        // SaveAddress 0x1234 at PS1 0x80193844 (a mapped global)
            0xC4, 0x72, 0x50, 0xFD, 0x1A, 0x02,        // SaveAddress 0x1234 at PS1 0x8019FD50 (the mod's own store)
            0xC1, 0x1B, 0x50, 0xFD, 0x1A, 0x02,        // LoadAddress it back
            0xAE, 0x73, 0x00, 0x00, 0x00,              // var 0x73 = 0
            0xC4, 0x73, 0xBA, 0x01, 0x70, 0x01,        // SaveAddress 0 at its base team (PS1 +0x1BA): not in the remaster
            0xC1, 0x1C, 0xBA, 0x01, 0x70, 0x01,        // LoadAddress the team the remaster has
            0xC7, 0x80, 0x03, 0x00,                    // RosterHasUnit sprite set 3
            0xAF, 0x19, 0x00, 0x00, 0x00,              // var 0x19 = var 0
            0xAE, 0x27, 0x00, 0x92, 0x00,              // var 0x27 (the event's number) = 0x92
            0xCD, 0x83, 0x05, 0x02,                    // PostEvent: event 0x92's word = (2 << 10) + 0x8305
            0x9A,                                      // ResetTextIncrement
            0x9B, 0x34, 0x12,                          // PushTextVal: word 0 = 0x1234
            0x9C, 0x15,                                // PushTextVar: word 1 = var 0x15 (the Brave read above, 0x46)
            0x9D, 0x10, 0x78, 0x56,                    // SetTextVal: word 0x10 = 0x5678
            0x9E, 0x11, 0x72,                          // SetTextVar: word 0x11 = var 0x72 (0x1234)
            0x14, 0x2A,                                // PlayTrack 0x2A
            0x8D, 0x28, 0x05, 0x1D,                    // Inventory: var 0x1D = item 5 owned and equipped (3 + 2)
            0x8D, 0x26, 0x05, 0x63,                    // item 5 + 99: capped at 99 with the 2 equipped, so 97 owned
            0x8D, 0x17, 0x07, 0x09,                    // item 7 - 9: 4 owned, so 0
            0x8D, 0x22, 0x05, 0x63,                    // var 0 = 97 + 2 >= 99
            0x8D, 0x30, 0x05, 0x00,                    // the Poachers' Den: not run (noted)
            0xCE, 0x01, 0x00,                          // LoadConditionals of event 1
            0xDB,
        ];
        var code = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(EventHooks.Size);
        *(int*)code = program.Length + 4;
        program.CopyTo(new Span<byte>(code + EventScript.Start, program.Length));
        var units = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(Battle.UnitCount * Battle.UnitSize);
        var roster = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(UnitTables.RosterSlots * UnitTables.RosterSize);
        for (var k = 0; k < Battle.UnitCount; k++)
            units[k * Battle.UnitSize + 1] = 0xFF;
        var unit3 = units + 3 * Battle.UnitSize;
        unit3[1] = 3; unit3[0x191] = 0x85; unit3[Battle.UnitBrave] = 70; unit3[Battle.UnitTeam] = 0x30;
        for (var k = 0; k < UnitTables.RosterSlots; k++)
            roster[k * UnitTables.RosterSize + 1] = 0xFF;
        roster[0] = 3; roster[1] = 0;
        var vars = new Dictionary<int, int>();
        var notes = new List<string>();
        var sum = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(4);
        var settings = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(Ps1Memory.MapSettingsBytes);
        var words = (int*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(Ps1Memory.ExtraWordsBytes);
        var conditionSets = (short*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(Ps1Memory.ConditionSetsBytes);
        var conditions = (short*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(Ps1Memory.ConditionsBytes);
        var script = new EventScript(id => vars.GetValueOrDefault(id), (id, v) => vars[id] = v,
                                     new Ps1Memory(units, roster, notes.Add).Map(0x80193844, 2, sum)
                                         .Map(Ps1Memory.MapSettings, Ps1Memory.MapSettingsBytes, settings)
                                         .Map(Ps1Memory.ExtraWords, Ps1Memory.ExtraWordsBytes, (byte*)words)
                                         .Map(Ps1Memory.ConditionSets, Ps1Memory.ConditionSetsBytes, (byte*)conditionSets)
                                         .Map(Ps1Memory.Conditions, Ps1Memory.ConditionsBytes, (byte*)conditions), notes.Add);
        var presets = new byte[2 * EventScript.PresetBytes];
        BitConverter.GetBytes((short)0).CopyTo(presets, EventScript.PresetBytes);              // event 1: set 0 at 0,
        BitConverter.GetBytes((short)0x0C).CopyTo(presets, EventScript.PresetBytes + 2);       // set 1 at 0x0C
        BitConverter.GetBytes((short)0x19).CopyTo(presets, EventScript.PresetBytes + 0x20);    // a record: type 0x19
        BitConverter.GetBytes((short)0x7F).CopyTo(presets, EventScript.PresetBytes + 0x21E);   // the last halfword
        script.Presets = presets;
        var natives = 0;
        script.Natives[0x80151000] = () => natives++;
        var music = -1;
        script.PlayMusic = track => music = track;
        var owned = new Dictionary<int, int> { [5] = 3, [7] = 4 };
        var equipped = new Dictionary<int, int> { [5] = 2 };
        script.Items = (item => owned.GetValueOrDefault(item), (item, n) => owned[item] = n,
                        item => owned.GetValueOrDefault(item) + equipped.GetValueOrDefault(item));
        var pc = EventScript.Start;
        var steps = 0;
        while (code[pc] != 0xDB && steps++ < 1000)
        {
            var to = script.Step(code, pc, EventHooks.Size);
            if (to < 0)
            {
                if (code[pc] == 0xBE)
                    vars[code[pc + 1] | code[pc + 2] << 8] = 0;
                to = EventScript.Next(code, pc);
            }
            pc = to;
        }
        string V(int id) => vars.TryGetValue(id, out var v) ? $"0x{v:X}" : "-";
        var seen = $"0x10 {V(0x10)}, 0x11 {V(0x11)}, 0x12 {V(0x12)}, 0x13 {V(0x13)}, 0x14 {V(0x14)}, unit {V(0x70)}, "
                   + $"X {unit3[Battle.UnitX]}, Brave {V(0x15)}, patched {V(0x16)}, unmapped {V(0x17)}, high {V(0x18)}, "
                   + $"has {V(0x19)}, global 0x{*(ushort*)sum:X}, own {V(0x1B)}, team 0x{unit3[Battle.UnitTeam]:X} read {V(0x1C)}, "
                   + $"map settings 0x{*(ushort*)(settings + 2 * 0x92):X}, list 0x{words[0]:X} 0x{words[1]:X} 0x{words[2]:X} 0x{words[0x10]:X} 0x{words[0x11]:X}, "
                   + $"music 0x{music:X}, items {V(0x1D)} {owned[5]} {owned[7]} {V(0)}, conditions {conditionSets[1]:X} {conditions[0]:X} {conditions[0xFF]:X}, "
                   + $"native {natives}, {notes.Count} note(s), ended {code[pc] == 0xDB}";
        Check($"a program: {seen}", seen == "0x10 0x1, 0x11 0x3, 0x12 0x7, 0x13 -, 0x14 0x4, unit 0x80190E0C, "
                                           + "X 42, Brave 0x46, patched 0x1234, unmapped 0x0, high 0x10000, has 0x1, global 0x1234, own 0x1234, team 0x30 read 0x30, "
                                           + "map settings 0x8B05, list 0x1234 0x46 0x0 0x5678 0x1234, music 0x2A, items 0x5 97 0 0x1, conditions C 19 7F, native 1, 6 note(s), ended True");
        // BlockLoop (0x2F) and LoadTileData/SaveTileData (0xE8/0xE9): 2A; F1 01 00; 2F 00 70 00 00; 2F 03 71 00 00;
        // E8 05 02 01 0F 00; E9 06 03 01 0F 00; 2B; DB.
        byte[] looping = [0x2A, 0xF1, 0x01, 0x00, 0x2F, 0x00, 0x70, 0x00, 0x00, 0x2F, 0x03, 0x71, 0x00, 0x00,
                          0xE8, 0x05, 0x02, 0x01, 0x0F, 0x00, 0xE9, 0x06, 0x03, 0x01, 0x0F, 0x00, 0x2B, 0xDB];
        new Span<byte>(code, EventHooks.Size).Clear();
        looping.CopyTo(new Span<byte>(code + EventScript.Start, looping.Length));
        int body = EventScript.Start + 1, loop = EventScript.Start + 4, countdown = EventScript.Start + 9;
        vars[0x70] = 1;
        var back = script.Step(code, loop, EventHooks.Size);
        vars[0x70] = 0;
        var through = script.Step(code, loop, EventHooks.Size);
        var counted = new List<int>();
        for (var k = 0; k < 4; k++)
            counted.Add(script.Step(code, countdown, EventHooks.Size));
        var tile = stackalloc byte[8];
        tile[2] = 0x2C;
        script.TileAt = (x, y, level) => x == 1 && y == 15 && level == 0 ? (nint)tile : 0;
        vars[6] = 0x77;
        script.Step(code, EventScript.Start + 14, EventHooks.Size);
        script.Step(code, EventScript.Start + 20, EventHooks.Size);
        Check($"BlockLoop: var set, back to {back} (the block at {body}); var 0, on to {through}; counting down from 3: "
              + $"{string.Join(" ", counted)}, var 0x{vars[0x71]:X}; LoadTileData: var 5 0x{vars[5]:X}; SaveTileData: the tile's byte 3 0x{tile[3]:X}",
              back == body && through == loop + 5 && string.Join(" ", counted) == $"{body} {body} {countdown + 5} {body}"
              && vars[5] == 0x2C && tile[3] == 0x77);
        System.Runtime.InteropServices.NativeMemory.Free(sum);
        System.Runtime.InteropServices.NativeMemory.Free(settings);
        System.Runtime.InteropServices.NativeMemory.Free(words);
        System.Runtime.InteropServices.NativeMemory.Free(conditionSets);
        System.Runtime.InteropServices.NativeMemory.Free(conditions);

        // The upgrade's words through the hooked variable executor, with a stand-in for the game's.
        Check($"the battle event variables' executor (0x{EventHooks.ArithmeticAt - game.Image.ImageStart:X}) is hooked",
              EventHooks.ArithmeticAt - game.Image.ImageStart == 0x242700 && ((byte*)EventHooks.ArithmeticAt)[0] is 0xE9 or 0xFF);
        var realArithmetic = EventHooks.GameArithmetic;
        EventHooks.GameArithmetic = &FakeArithmetic;
        var savedWords = new Span<byte>(EventHooks.Words, Ps1Memory.ExtraWordsBytes).ToArray();
        var run = (delegate* unmanaged<uint, uint, uint, nint, nint, void>)EventHooks.ArithmeticAt;
        _arithmetic.Clear();
        run(0xBE, 0xB05, 0, 0, 0);    // the setter: ZERO, then ADD the value
        run(0xB0, 0xB05, 7, 0, 0);
        run(0xB4, 0xB05, 6, 0, 0);    // MULT 6: 42
        run(0xB3, 0xB06, 0xB05, 0, 0); // SUBVar: word 6 = 0 - 42
        run(0xB1, 0x10, 0xB05, 0, 0); // ADDVar into an ordinary variable: the game adds 42 as a literal
        run(0xB0, 0x11, 3, 0, 0);     // nothing of the upgrade's: the game's own
        var arithmetic = $"0x{EventHooks.Words[5]:X} {EventHooks.Words[6]}, the game got {string.Join(" ", _arithmetic)}";
        new Span<byte>(savedWords).CopyTo(new Span<byte>(EventHooks.Words, Ps1Memory.ExtraWordsBytes));
        EventHooks.GameArithmetic = realArithmetic;
        Check($"the words 0xB00 on in the mod's store, others the game's: {arithmetic}",
              arithmetic == "0x2A -42, the game got B0:10:2A B0:11:3");
        var apply = string.Join(" ", new uint[] { 0xB6, 0xB8, 0xBA, 0xBC }.Select(op => EventHooks.Apply(op, 45, 6)));
        Check($"the arithmetic: 45 / 6, % 6, & 6, | 6 = {apply}; / 0 leaves it ({EventHooks.Apply(0xB6, 45, 0)})",
              apply == "7 3 4 47" && EventHooks.Apply(0xB6, 45, 0) == 45);
        // A battle unit's name in the PS1's layout (three words at +0x12C) comes out in the remaster's UTF-8 at +0x14C;
        // read back as written.
        var named = new Ps1Memory(units, roster, _ => { });
        var nameAt = Ps1Memory.Units + 3 * Ps1Memory.UnitSize + Ps1Memory.UnitNameAt;
        named.Write(nameAt, 4, 0x3230350A);
        named.Write(nameAt + 4, 4, 0x14272835);
        named.Write(nameAt + 8, 4, unchecked((int)0xFE2C3532));
        var unitName = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)(unit3 + Ps1Memory.RemasterUnitNameAt));
        Check($"a battle unit's name written as the PS1 has it: \"{unitName}\" in the remaster's, read back "
              + $"0x{named.Read(nameAt, 4):X8}", unitName == "ArmoredKori" && named.Read(nameAt, 4) == 0x3230350A);
        // A roster unit's JP for its job (PS1 +0x6E + 2 x index) and its total (+0x96 + 2 x index): the remaster's +0x80
        // and +0xAE, stopped at 9999.
        var jpAt = Ps1Memory.Roster + 2 * Ps1Memory.RosterSize + Ps1Memory.JpAt + 6;
        named.Write(jpAt, 2, 9990 + 50);
        named.Write(jpAt + (Ps1Memory.TotalJpAt - Ps1Memory.JpAt), 2, 1234);
        var slot2 = roster + 2 * UnitTables.RosterSize;
        var jp = $"{*(ushort*)(slot2 + Ps1Memory.RemasterJpAt + 6)} {*(ushort*)(slot2 + Ps1Memory.RemasterTotalJpAt + 6)}";
        Check($"a roster unit's JP for job index 3 written as the PS1 has it (10040, then a total of 1234): {jp} in the "
              + $"remaster's, read back {named.Read(jpAt, 2)}", jp == "9999 1234" && named.Read(jpAt, 2) == 9999);
        // The same for a battle unit's (PS1 +0xDC and +0x104, the remaster's +0xF0 and +0x11E).
        var unitJpAt = Ps1Memory.Units + 3 * Ps1Memory.UnitSize + Ps1Memory.UnitJpAt + 6;
        named.Write(unitJpAt, 2, 9990 + 50);
        named.Write(unitJpAt + (Ps1Memory.UnitTotalJpAt - Ps1Memory.UnitJpAt), 2, 1234);
        var unitJp = $"{*(ushort*)(unit3 + Ps1Memory.RemasterUnitJpAt + 6)} {*(ushort*)(unit3 + Ps1Memory.RemasterUnitTotalJpAt + 6)}";
        Check($"a battle unit's JP for job index 3 written as the PS1 has it (10040, then a total of 1234): {unitJp} in the "
              + $"remaster's, read back {named.Read(unitJpAt, 2)}", unitJp == "9999 1234" && named.Read(unitJpAt, 2) == 9999);
        foreach (var p in new nint[] { (nint)cell, (nint)table, (nint)marked, (nint)other, (nint)code, (nint)units, (nint)roster })
            System.Runtime.InteropServices.NativeMemory.Free((void*)p);
        return failures;
    }

    private static readonly List<string> _arithmetic = [];

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void FakeArithmetic(uint op, uint dest, uint source, nint r9, nint flag) =>
        _arithmetic.Add($"{op:X}:{dest:X}:{source:X}");

    /// <summary>0xD1 n: on to the instruction after the next 0xD2 n, or to the END without one.</summary>
    public static int PhaseBlocks(Log log)
    {
        // D1 02; D2 00; F1 1e 00; D2 02; F1 3c 00; D2 ff; DB
        var code = stackalloc byte[] { 0xD1, 0x02, 0xD2, 0x00, 0xF1, 0x1E, 0x00, 0xD2, 0x02, 0xF1, 0x3C, 0x00, 0xD2, 0xFF, 0xDB };
        var found = EventHooks.PhaseBlock(code, 0, 15);
        code[1] = 0xCC;
        var missing = EventHooks.PhaseBlock(code, 0, 15);
        var ok = found == 9 && missing == 14;
        log.Info($"self-test: phase blocks, phase 2 goes on at {found}, a missing phase at {missing} (the END){(ok ? "" : ": WRONG")}");
        return ok ? 0 : 1;
    }

    /// <summary>GenerateUnit (0xE0): the remaster's unit making found, and the record built from the parameters (a
    /// special character, a generic enemy), from roster slot 0, and a roster search finding nothing.</summary>
    public static int GenerateUnit(Game game, Log log)
    {
        var failures = 0;
        void Check(string name, bool ok)
        {
            if (!ok) failures++;
            log.Info($"self-test: GenerateUnit, {name}{(ok ? "" : ": WRONG")}");
        }
        if (!CreateUnit.Found)
            CreateUnit.Find(game.Image);
        var places = $"0x{CreateUnit.MakeAt - game.Image.ImageStart:X} 0x{CreateUnit.GraphicAt - game.Image.ImageStart:X} "
                     + $"0x{CreateUnit.FinishAt - game.Image.ImageStart:X} 0x{CreateUnit.CurrentAt - game.Image.ImageStart:X}";
        Check($"the unit making, its graphic and finishing steps, the current record: {places}",
              places == "0x275CA4 0x278EDC 0x278E0C 0x11A3868");
        var memory = new Dictionary<uint, int>();
        int Read(uint address) => memory.GetValueOrDefault(address);
        var record = new byte[CreateUnit.RecordSize];
        // A special character: job and sprite set 1 (Ramza), male, unit id 1 in slot 0 at (5, 1).
        var (made, _) = CreateUnit.Build(Convert.FromHexString("018001000000000005010040"), record, Read);
        Check($"a special character from its parameters: {Convert.ToHexString(record)}", made
              && Convert.ToHexString(record) == "01800164FEFEFEFE000001FEFE01FE01FE01FEFEFEFEFE0000000000FEFF00000000000000000000");
        // A generic enemy: a Knight (0x4C), male, team 1 (p1 0x81).
        (made, _) = CreateUnit.Build(Convert.FromHexString("4C8181060000000006010240"), record, Read);
        Check($"a generic enemy from its parameters: sprite set 0x{record[0]:X2}, gender 0x{record[1]:X2}, name 0x{record[2]:X2}, "
              + $"job 0x{record[0xA]:X2}, team byte 0x{record[0x18]:X2}",
              made && record[0] == 0x80 && record[1] == 0x80 && record[2] == 0xFF && record[0xA] == 0x4C && record[0x18] == 0x10);
        // Roster slot 0 (Ramza, level 12, name id 1) as unit 0x9A.
        memory[Ps1Memory.Roster] = 1;
        memory[Ps1Memory.Roster + 4] = 0x80;
        memory[Ps1Memory.Roster + 0x16] = 12;
        memory[Ps1Memory.Roster + 0xCE] = 1;
        memory[Ps1Memory.Roster + 2] = 1;
        var (fromRoster, source) = CreateUnit.Build(Convert.FromHexString("00009A100000000009080002"), record, Read);
        Check($"roster slot 0: sprite set 0x{record[0]:X2}, gender 0x{record[1]:X2}, name 0x{record[2]:X2}, level {record[3]}, "
              + $"job 0x{record[0xA]:X2}, flags 0x{record[0x18]:X2}, from 0x{source:X8}",
              fromRoster && source == Ps1Memory.Roster && record[0] == 1 && record[1] == 0x80 && record[2] == 1 && record[3] == 12
              && record[0xA] == 1 && record[0x18] == 0xE);
        // A roster search (no flags) for sprite set 0x30, which no slot has.
        var (found, _) = CreateUnit.Build(Convert.FromHexString("300000020000000000000000"), record, Read);
        Check($"a roster search finding nothing makes nothing ({found}); graphics 0xB5 0x{CreateUnit.Graphic(0xB5, 0x40):X2}, "
              + $"0xB6 female 0x{CreateUnit.Graphic(0xB6, 0x40):X2}, 0x90 male 0x{CreateUnit.Graphic(0x90, 0x80):X2}",
              !found && CreateUnit.Graphic(0xB5, 0x40) == 0x83 && CreateUnit.Graphic(0xB6, 0x40) == 0x85
              && CreateUnit.Graphic(0x90, 0x80) == 0x60);
        return failures;
    }
}
