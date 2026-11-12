namespace IvaliceCore;

/// <summary>The battle event variables: the game's getter (0x2332D8) and setter (0x233368), and their words.</summary>
internal static unsafe class EventVariables
{
    /// <summary>The getter and setter. The getter: for var 0x22 it reads 0x24 and 0x23 through itself, then stores
    /// through the setter.</summary>
    internal static (nint Get, nint Set) Find(GameImage image)
    {
        var body = image.FindUnique("8B F9 8B 30 83 F9 22 75 2B 8D 4F 02 E8 ?? ?? ?? ?? 8B D8 8D 4F 01");
        var get = body - 0x16;
        var set = image.FindUnique("48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 30 48 8B 05 ?? ?? ?? ?? 85 D2 0F B6 DA");
        if (GameImage.Rip(body + 12, 1, 5) != get || GameImage.Rip(get + 0x45, 1, 5) != set)
            throw new InvalidDataException("the event variable getter and setter aren't laid out as this DLL expects");
        return (get, set);
    }

    /// <summary>
    /// [0x7830D8], the pointer to the variables' 32-bit words (var 0 at +0, var 1 at +4, as the comparisons read them;
    /// the PS1's 0x8005771C), from the setter's mov rax, [0x7830D8] at +0x14.
    /// </summary>
    internal static int** Words(nint set)
    {
        if (*(byte*)(set + 0x14) != 0x48)
            throw new InvalidDataException("the event variable setter isn't laid out as this DLL expects");
        return (int**)GameImage.Rip(set + 0x14, 3, 7);
    }
}
