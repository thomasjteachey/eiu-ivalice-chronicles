namespace IvaliceCore;

/// <summary>
/// Stubs between the game's code and C#. The exe is built with whole-program optimisation, so a
/// caller can keep a scratch register (r10, r11, xmm0–5) across a call to a function it knows
/// doesn't touch it; a C# function clobbers them all. Check a hooked function's callers with
/// ExeTool's liveregs, and put a stub in front of the C# when any of them keeps one.
/// </summary>
internal static unsafe class Stub
{
    private const int Size = 160;

    /// <summary>
    /// A stub that calls <paramref name="managed"/> with rcx, rdx, r8 and r9 as they came (or with
    /// r8d loaded from the caller's [rsp+<paramref name="callerSlotToR8"/>]) and keeps r10, r11 and
    /// xmm0–5. rax is the C# function's result. For functions of up to four arguments. With
    /// <paramref name="near"/>, it's placed within a rel32 call of that address.
    /// </summary>
    public static nint KeepingRegisters(nint managed, int callerSlotToR8 = -1, nint near = 0)
    {
        var code = new List<byte>
        {
            0x41, 0x52, 0x41, 0x53,                   // push r10; push r11 (16-aligned after the next)
            0x48, 0x81, 0xEC, 0x88, 0x00, 0x00, 0x00, // sub rsp, 0x88: shadow space, xmm0–5, padding
        };
        SaveXmm(code, 0x20);
        if (callerSlotToR8 >= 0)
        {
            code.AddRange([0x44, 0x8B, 0x84, 0x24]);  // mov r8d, [rsp + 0x98 + 8 + slot]
            code.AddRange(BitConverter.GetBytes(0xA0 + callerSlotToR8));
        }
        code.AddRange([0x48, 0xB8]);                  // mov rax, managed
        code.AddRange(BitConverter.GetBytes((ulong)managed));
        code.AddRange([0xFF, 0xD0]);                  // call rax
        RestoreXmm(code, 0x20);
        code.AddRange([0x48, 0x81, 0xC4, 0x88, 0x00, 0x00, 0x00, 0x41, 0x5B, 0x41, 0x5A, 0xC3]); // add rsp; pop r11; pop r10; ret
        var stub = near != 0 ? GameImage.AllocateCodeNear(near, Size) : GameImage.AllocateCode(Size);
        if (code.Count > Size)
            throw new InvalidOperationException("the stub is too big");
        code.ToArray().CopyTo(new Span<byte>(stub, Size));
        return (nint)stub;
    }

    /// <summary>movdqu [rsp+at+16k], xmmk for k = 0..5.</summary>
    public static void SaveXmm(List<byte> code, int at)
    {
        for (var x = 0; x < 6; x++)
            code.AddRange(MovdquRsp(0x7F, x, at + 0x10 * x));
    }

    /// <summary>movdqu xmmk, [rsp+at+16k] for k = 0..5.</summary>
    public static void RestoreXmm(List<byte> code, int at)
    {
        for (var x = 0; x < 6; x++)
            code.AddRange(MovdquRsp(0x6F, x, at + 0x10 * x));
    }

    private static byte[] MovdquRsp(byte op, int xmm, int disp) => disp < 0x80
        ? [0xF3, 0x0F, op, (byte)(0x44 | (xmm << 3)), 0x24, (byte)disp]
        : [0xF3, 0x0F, op, (byte)(0x84 | (xmm << 3)), 0x24, (byte)disp, (byte)(disp >> 8), 0x00, 0x00];
}

internal static class ListBytes
{
    /// <summary>Writes a little-endian int into a byte list at <paramref name="at"/>.</summary>
    public static void PutInt(this List<byte> list, int at, int value)
    {
        for (var k = 0; k < 4; k++)
            list[at + k] = (byte)(value >> (8 * k));
    }
}
