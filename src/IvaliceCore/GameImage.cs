using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IvaliceCore;

/// <summary>
/// The game's exe as mapped in memory: find code and data by their bytes, and
/// write over them. Also works on an exe mapped from disk (the offline check),
/// where pointers stored in its data aren't relocated.
/// </summary>
internal sealed unsafe class GameImage
{
    private readonly record struct Section(string Name, nint Start, int Size);

    private readonly List<Section> _sections = [];

    /// <summary>File name of the exe, lower case (fft_enhanced.exe or fft_classic.exe).</summary>
    public string ExeName { get; }

    /// <summary>
    /// Difference between where the image is and where its header says it was
    /// built for. 0 in the game (the loader relocates pointers and updates the
    /// header); the mapping offset for an exe mapped from disk.
    /// </summary>
    public nint Delta { get; }

    /// <summary>Where the image starts in memory (image-relative offsets count from here).</summary>
    public nint ImageStart { get; }

    private GameImage(nint baseAddress, string exeName)
    {
        ExeName = exeName;
        ImageStart = baseAddress;
        var b = (byte*)baseAddress;
        var nt = b + *(int*)(b + 0x3C);
        var sectionCount = *(ushort*)(nt + 6);
        var optionalSize = *(ushort*)(nt + 20);
        Delta = baseAddress - (nint)(*(ulong*)(nt + 24 + 24));
        var table = nt + 24 + optionalSize;
        for (var i = 0; i < sectionCount; i++)
        {
            var s = table + 40 * i;
            var name = Marshal.PtrToStringAnsi((nint)s, 8).TrimEnd('\0');
            _sections.Add(new Section(name, baseAddress + *(int*)(s + 12), *(int*)(s + 8)));
        }
    }

    /// <summary>The running game.</summary>
    public static GameImage Main()
    {
        var module = Process.GetCurrentProcess().MainModule
                     ?? throw new InvalidOperationException("no main module");
        return new GameImage(module.BaseAddress, Path.GetFileName(module.FileName).ToLowerInvariant());
    }

    /// <summary>
    /// An exe laid out from disk as the loader would (headers and sections at their
    /// addresses, no relocations or imports), without running it, in private memory
    /// that hooks can patch. For the offline check.
    /// </summary>
    public static GameImage FromFile(string path)
    {
        var file = File.ReadAllBytes(path);
        var nt = BitConverter.ToInt32(file, 0x3C);
        var sizeOfImage = BitConverter.ToInt32(file, nt + 24 + 56);
        var sizeOfHeaders = BitConverter.ToInt32(file, nt + 24 + 60);
        var image = VirtualAlloc(0, sizeOfImage, MemCommitReserve, PageExecuteReadWrite);
        if (image == 0)
            throw new OutOfMemoryException($"can't lay out {path} ({Marshal.GetLastPInvokeError()})");
        Marshal.Copy(file, 0, image, sizeOfHeaders);
        var count = BitConverter.ToUInt16(file, nt + 6);
        var table = nt + 24 + BitConverter.ToUInt16(file, nt + 20);
        for (var i = 0; i < count; i++)
        {
            var s = table + 40 * i;
            var virtualSize = BitConverter.ToInt32(file, s + 8);
            var rva = BitConverter.ToInt32(file, s + 12);
            var rawSize = BitConverter.ToInt32(file, s + 16);
            var raw = BitConverter.ToInt32(file, s + 20);
            Marshal.Copy(file, raw, image + rva, Math.Min(rawSize, virtualSize));
        }
        return new GameImage(image, Path.GetFileName(path).ToLowerInvariant());
    }

    /// <summary>A code or data pointer as stored in the image, made usable here.</summary>
    public nint Pointer(nint stored) => stored + Delta;

    /// <summary>Whether an address is in the image's code (.text).</summary>
    public bool IsCode(nint address)
    {
        var text = _sections.Find(x => x.Name == ".text");
        return address >= text.Start && address < text.Start + text.Size;
    }

    /// <summary>
    /// A code pointer read from a table in the image, made callable: one the image stored
    /// needs <see cref="Delta"/> (in an exe mapped from disk), one the mod wrote doesn't.
    /// </summary>
    public nint StoredCode(nint stored) => IsCode(stored + Delta) ? stored + Delta : stored;

    /// <summary>
    /// Address of the one match of a byte pattern ("48 8D 0D ?? ?? ?? ??", ?? = any byte)
    /// in a section. Throws if it's missing or occurs more than once, since either
    /// means the game changed.
    /// </summary>
    public nint FindUnique(string pattern, string section = ".text")
    {
        var found = FindAll(pattern, section, 2);
        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidDataException($"pattern not found: {pattern}"),
            _ => throw new InvalidDataException($"pattern found more than once: {pattern}"),
        };
    }

    /// <summary>The matches of a pattern that occurs exactly <paramref name="count"/> times (copies of one routine), in order.</summary>
    public IReadOnlyList<nint> FindExactly(string pattern, int count, string section = ".text")
    {
        var found = FindAll(pattern, section, count + 1);
        if (found.Count != count)
            throw new InvalidDataException($"pattern found {found.Count} times, not {count}: {pattern}");
        return found;
    }

    /// <summary>Throws unless the code has at least one match of a pattern (to confirm an offset it uses).</summary>
    public void Expect(string pattern, string what, string section = ".text")
    {
        if (FindAll(pattern, section, 1).Count == 0)
            throw new InvalidDataException($"no code uses {what} as this DLL expects: {pattern}");
    }

    private List<nint> FindAll(string pattern, string section, int limit)
    {
        var (bytes, wild) = Parse(pattern);
        // Anchor on the longest run of fixed bytes, then check the rest.
        int anchor = 0, anchorLength = 0;
        for (int i = 0, run = 0; i < bytes.Length; i++)
        {
            run = wild[i] ? 0 : run + 1;
            if (run > anchorLength) (anchor, anchorLength) = (i - run + 1, run);
        }
        var s = _sections.Find(x => x.Name == section);
        if (s.Size == 0)
            throw new InvalidDataException($"the exe has no {section} section");
        var span = new ReadOnlySpan<byte>((void*)s.Start, s.Size);
        var key = bytes.AsSpan(anchor, anchorLength);
        var found = new List<nint>();
        for (var from = 0; found.Count < limit;)
        {
            var at = span[from..].IndexOf(key);
            if (at < 0) break;
            var start = from + at - anchor;
            from += at + 1;
            if (start >= 0 && start + bytes.Length <= span.Length && Matches(span[start..], bytes, wild))
                found.Add(s.Start + start);
        }
        return found;
    }

    /// <summary>
    /// Checks that the code at <paramref name="at"/> matches a pattern and returns
    /// the targets of its calls and jumps, in order: every "E8 ?? ?? ?? ??" or
    /// "E9 ?? ?? ?? ??" in the pattern. Throws if the code doesn't match.
    /// </summary>
    public static nint[] Calls(nint at, string pattern, string what)
    {
        var (bytes, wild) = Parse(pattern);
        if (!Matches(new ReadOnlySpan<byte>((void*)at, bytes.Length), bytes, wild))
            throw new InvalidDataException($"{what} at 0x{at:X} isn't the code this DLL expects");
        var calls = new List<nint>();
        for (var k = 0; k + 4 < bytes.Length; k++)
            if (!wild[k] && bytes[k] is 0xE8 or 0xE9 && wild[k + 1] && wild[k + 2] && wild[k + 3] && wild[k + 4])
                calls.Add(Rip(at + k, 1, 5));
        return calls.ToArray();
    }

    /// <summary>Target of a RIP-relative operand: the instruction's end plus its 32-bit displacement.</summary>
    public static nint Rip(nint instruction, int displacementOffset, int instructionLength) =>
        instruction + instructionLength + *(int*)(instruction + displacementOffset);

    private static (byte[] Bytes, bool[] Wild) Parse(string pattern)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (tokens.Select(t => t == "??" ? (byte)0 : Convert.ToByte(t, 16)).ToArray(),
                tokens.Select(t => t == "??").ToArray());
    }

    private static bool Matches(ReadOnlySpan<byte> code, byte[] bytes, bool[] wild)
    {
        for (var k = 0; k < bytes.Length; k++)
            if (!wild[k] && code[k] != bytes[k])
                return false;
        return true;
    }

    /// <summary>Run <paramref name="write"/> with [address, address + size) writable, then restore its protection.</summary>
    public static void Unprotected(nint address, int size, Action write)
    {
        // Image pages take PAGE_READWRITE in the game; a mapped-from-disk image needs copy-on-write.
        if (!VirtualProtect(address, size, PageReadWrite, out var old)
            && !VirtualProtect(address, size, PageWriteCopy, out old))
            throw new InvalidOperationException($"VirtualProtect failed ({Marshal.GetLastPInvokeError()})");
        try
        {
            write();
        }
        finally
        {
            VirtualProtect(address, size, old, out _);
        }
    }

    /// <summary>
    /// Overwrite code with <paramref name="code"/>. The page stays executable while it's written,
    /// since another thread may be running code on it.
    /// </summary>
    public static void WriteCode(nint address, ReadOnlySpan<byte> code)
    {
        if (!VirtualProtect(address, code.Length, PageExecuteReadWrite, out var old))
            throw new InvalidOperationException($"VirtualProtect failed ({Marshal.GetLastPInvokeError()})");
        try
        {
            code.CopyTo(new Span<byte>((void*)address, code.Length));
        }
        finally
        {
            VirtualProtect(address, code.Length, old, out _);
            FlushInstructionCache(GetCurrentProcess(), address, code.Length);
        }
    }

    /// <summary>Fresh executable memory for small code stubs the mod writes; never freed (they live as long as the game).</summary>
    public static byte* AllocateCode(int size)
    {
        var code = VirtualAlloc(0, size, MemCommitReserve, PageExecuteReadWrite);
        if (code == 0)
            throw new OutOfMemoryException($"can't allocate {size} bytes for code ({Marshal.GetLastPInvokeError()})");
        return (byte*)code;
    }

    /// <summary>
    /// Executable memory within reach of a rel32 call from <paramref name="near"/> (±2 GB), for a
    /// stub a patched call goes to; never freed. Tries 1 MB steps below, then above.
    /// </summary>
    public static byte* AllocateCodeNear(nint near, int size)
    {
        for (long step = 1; step < 0x700; step++)
            foreach (var at in new[] { near - step * 0x100000, near + step * 0x100000 })
            {
                var code = VirtualAlloc((nint)(at & ~0xFFFFL), size, MemCommitReserve, PageExecuteReadWrite);
                if (code != 0)
                    return (byte*)code;
            }
        throw new OutOfMemoryException($"no free memory within reach of 0x{near:X}");
    }

    /// <summary>Make [address, address + size) writable and leave it so (for data the mod changes during play).</summary>
    public static void MakeWritable(nint address, int size)
    {
        if (!VirtualProtect(address, size, PageReadWrite, out _) && !VirtualProtect(address, size, PageWriteCopy, out _))
            throw new InvalidOperationException($"VirtualProtect failed ({Marshal.GetLastPInvokeError()})");
    }

    private const uint PageReadWrite = 0x04;
    private const uint PageWriteCopy = 0x08;
    private const uint MemCommitReserve = 0x3000;
    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(nint address, nint size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nint size, uint type, uint protect);

    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(nint process, nint address, nint size);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
