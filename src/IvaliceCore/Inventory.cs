namespace IvaliceCore;

/// <summary>The party's items in the remaster.</summary>
internal static unsafe class Inventory
{
    /// <summary>The inventory (0x11A7C00, an owned count per item) and the game's count of an item owned and equipped
    /// (0x278FEC: the inventory, the roster's units not in the battle and the player's battle units).</summary>
    internal static (nint Inventory, nint Owned) FindCount(GameImage image)
    {
        // The count: movzx r11d, cx; lea rbx, [the image]; xor r8d, r8d; movzx edx, byte [r11+rbx+the inventory].
        var owned = image.FindUnique("48 89 5C 24 08 44 0F B7 D9 48 8D 1D ?? ?? ?? ?? 45 33 C0 41 0F B6 94 1B");
        if (GameImage.Rip(owned + 9, 3, 7) != image.ImageStart)
            throw new InvalidDataException("the item count doesn't index from the image");
        return (image.ImageStart + *(int*)(owned + 0x18), owned);
    }
}
