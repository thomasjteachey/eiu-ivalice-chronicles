using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EventInstructionUpgrade;

/// <summary>
/// The PS1's button words (PS1 0x80045944 held, 0x80045948 held the frame before, 0x8004594C pressed: the first pad
/// in the low half, in the PS1's layout) for the upgrade's events, made from the remaster's input whenever an event reads them.
/// The remaster makes its own copy of those words only in a battle's frame (0x37FB7C, from the first pad's record), not
/// during a scene that runs outside a battle's turns, such as ReMixed's dispatch tavern, whose unit picker waited for
/// good on them (seen on the PC, 2026-11-11). So the held word is the pad's record ([0x2E942E8]: +2 the high byte, +3
/// the low, a bit clear while held; Steam Input fills it from a controller) together with the remaster's input action
/// for each button, as its own screens read their keys (the UI's action check 0x0F0D00, on the input state
/// [0x3CD9EE8] + 8: the keyboard's keys and the pad's buttons through the game's key settings; its action test
/// 0x0FC598 goes there for button 0, but says no outright in the input mode the PC's test runs are in), and pressed is
/// what's held now and wasn't the frame before.
/// </summary>
internal static unsafe class Ps1Pad
{
    /// <summary>The PS1's buttons and the remaster's action for each (the pairs its own code asks its action test
    /// about most: 116 calls for Circle's 0x1A, 86 for Cross's 0x1B, 23 for Up's 0x21 and so on; L1 and R1 the pair its
    /// screens switch tabs by, 0x2B and 0x2C). The tavern's unit picker moves by Left and Right, steps through the
    /// roster by L1 and R1, and takes Circle, Cross, Triangle and Start.</summary>
    internal static readonly (int Bit, int Action)[] Actions =
    [
        (0x1000, 0x21), (0x4000, 0x22), (0x8000, 0x23), (0x2000, 0x24), // up, down, left, right
        (0x0020, 0x1A), (0x0040, 0x1B), (0x0080, 0x1D), (0x0010, 0x1E), // circle, cross, square, triangle
        (0x0100, 0x20), (0x0800, 0x1F),                                  // select, start
        (0x0004, 0x2B), (0x0008, 0x2C),                                  // L1, R1
    ];

    // The battle frame (0x37FB7C): sub rsp, 0x28; mov rax, [second pad]; mov rcx, [first pad]; mov edx, [held];
    // mov [held before], edx; movzx r8d, byte [rax+2].
    internal const string BattleFramePattern =
        "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 8B 15 ?? ?? ?? ?? 89 15 ?? ?? ?? ?? 44 0F B6 40 02";
    // The action test (0x0FC598: the pad word, a button, an action, a mode): push rbx; sub rsp, 0x20;
    // mov r10, [the input state]; mov r11d, ecx; mov ebx, r8d; movzx eax, byte [r10+0x17]; cmp byte [rax+r10+0x14], 2.
    internal const string ActionTestPattern =
        "40 53 48 83 EC 20 4C 8B 15 ?? ?? ?? ?? 44 8B D9 41 8B D8 41 0F B6 42 17 42 80 7C 10 14 02";
    /// <summary>A read this long after the last starts a new frame (the game runs at 60 a second; an event's reads of
    /// one frame come microseconds apart).</summary>
    private const double FrameMs = 8;

    /// <summary>The three words, in the PS1's order (held, held before, pressed).</summary>
    internal static uint* Words { get; private set; }
    private static nint* _firstPad, _input;
    private static delegate* unmanaged<nint, int, byte> _check;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _last = -1;
    /// <summary>In the action test: the input state's pointer (mov rcx, [the input state] at +0x4A) and its jump to the
    /// UI's action check (jmp 0x0F0D00 at +0x5C), where it goes for button 0 (past where a hook goes).</summary>
    internal const int InputAt = 0x4A, CheckJumpAt = 0x5C;

    public static void Install(GameImage image)
    {
        Words = (uint*)NativeMemory.AllocZeroed(12);
        // Found before ReMixed's test autopilot hooks both (their patterns start where its hooks go): this mod loads first.
        _firstPad = (nint*)GameImage.Rip(image.FindUnique(BattleFramePattern) + 11, 3, 7);
        var test = image.FindUnique(ActionTestPattern);
        if (*(byte*)(test + InputAt) != 0x48 || *(byte*)(test + CheckJumpAt) != 0xE9)
            throw new InvalidDataException("the action test isn't laid out as this DLL expects");
        _input = (nint*)GameImage.Rip(test + InputAt, 3, 7);
        _check = (delegate* unmanaged<nint, int, byte>)GameImage.Rip(test + CheckJumpAt, 1, 5);
    }

    /// <summary>For the offline check: the UI's action check and the input state's pointer, as found.</summary>
    internal static (nint Check, nint Input) Places => ((nint)_check, (nint)_input);

    /// <summary>The words made again for this frame (called before ReMixed's code reads any of them).</summary>
    internal static void Refresh()
    {
        if (Words == null)
            return;
        var now = Clock.Elapsed.TotalMilliseconds;
        var held = Held();
        if (_last < 0 || now - _last > FrameMs)
            Words[1] = Words[0];
        _last = now;
        Words[0] = held;
        Words[2] = held & ~Words[1];
    }

    /// <summary>The PS1 buttons held now: the pad's record and the remaster's actions.</summary>
    internal static uint Held()
    {
        uint held = 0;
        var pad = _firstPad == null ? 0 : *_firstPad;
        if (pad == 0)  // not the running game (the offline check's copy: its globals are empty)
            return 0;
        held |= ~(uint)(*(byte*)(pad + 2) << 8 | *(byte*)(pad + 3)) & 0xFFFF;
        var input = _input == null ? 0 : *_input;
        if (_check != null && input != 0)
            foreach (var (bit, action) in Actions)
                if (_check(input + 8, action) != 0)
                    held |= (uint)bit;
        return held;
    }
}
