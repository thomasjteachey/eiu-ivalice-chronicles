namespace IvaliceCore;

/// <summary>
/// The battle map: its size, its tiles (8 bytes each, +0 &amp; 0x3F terrain), the
/// terrain-to-Elemental table, and the panel a unit's action targets. PS1
/// addresses in the comments; remaster RVAs as of 2026-11.
/// </summary>
internal sealed unsafe class BattleMap
{
    private byte* _width;   // PS1 0x800E4E9C; 0xC6AD6A
    private byte* _height;  // PS1 0x800E4EA0; 0xC6AD6B
    private byte* _tiles;   // PS1 0x8018F8CC; .data 0xD8DCB0
    private delegate* unmanaged<uint, byte*> _getUnit; // unit by battle index (PS1 0x8007A724; 0x260ABC)

    /// <summary>The map's size in tiles.</summary>
    public int Width => *_width;
    public int Height => *_height;

    /// <summary>Where the width and the tiles live, for patches to check that code reads the same ones.</summary>
    internal nint WidthAddress => (nint)_width;
    internal nint TilesAddress => (nint)_tiles;

    /// <summary>For the offline check, which has no map loaded: set the map's size.</summary>
    internal void SetSize(byte width, byte height)
    {
        *_width = width;
        *_height = height;
    }

    /// <summary>The Elemental ability (0x7E–0x89) for each terrain type (PS1 SCUS 0x8005E9D0; 0x67F750).</summary>
    public byte* TerrainAbility { get; private set; }

    public static BattleMap Find(GameImage image)
    {
        // Clamping a position to the map: x below the width, y below the height.
        var clamp = image.FindUnique("0F B6 3D ?? ?? ?? ?? 8D 47 FF 3B F0 0F 4C C6 85 C0 0F 4F D0 0F B6 05 ?? ?? ?? ?? FF C8");
        // Action setup: the Elemental for the terrain under the acting unit (y × width + level × 256 + x).
        var elemental = image.FindUnique(
            "0F B6 4A 50 0F B6 05 ?? ?? ?? ?? 0F AF C8 0F B6 42 51 C1 E8 07 C1 E0 08 03 C8 0F B6 42 4F 03 C8 " +
            "41 0F B6 8C CC ?? ?? ?? ?? 83 E1 3F 42 0F B6 BC 21 ?? ?? ?? ??");
        // The panel an action targets: a panel (type 5) or the unit with that index.
        image.Expect("80 B9 AA 01 00 00 05 75 ?? 0F BF 81 AC 01 00 00 44 6B C8 1C 0F BF 43 4C 44 2B C8 0F BF 81 B0 01 00 00",
                     "a unit's action target (+0x1AA type, +0x1AC x, +0x1B0 y)");
        var getUnit = image.FindUnique("0F B6 89 AB 01 00 00 E8 ?? ?? ?? ?? 45 33 C9 48 85 C0 74 ?? 44 0F BF 48 4C");

        var map = new BattleMap
        {
            _width = (byte*)GameImage.Rip(clamp, 3, 7),
            _height = (byte*)GameImage.Rip(clamp + 20, 3, 7),
            _tiles = (byte*)(image.ImageStart + *(int*)(elemental + 37)),
            TerrainAbility = (byte*)(image.ImageStart + *(int*)(elemental + 49)),
            _getUnit = (delegate* unmanaged<uint, byte*>)GameImage.Rip(getUnit + 7, 1, 5),
        };
        if ((byte*)GameImage.Rip(elemental + 4, 3, 7) != map._width || map._height != map._width + 1)
            throw new InvalidDataException("the map's size and its tile lookup disagree");
        return map;
    }

    /// <summary>The tile at a position, or null off the map (PS1 0x80183FB4).</summary>
    public byte* TileAt(int x, int y, int level)
    {
        if (x < 0 || y < 0 || level is < 0 or > 1 || x >= *_width || y >= *_height)
            return null;
        return _tiles + 8 * (level * 256 + y * *_width + x);
    }

    /// <summary>The tile a unit stands on.</summary>
    public byte* TileUnder(byte* unit) =>
        TileAt(unit[Battle.UnitX], unit[Battle.UnitY], unit[Battle.UnitLevel] >> 7);

    /// <summary>
    /// The panel a unit's action targets (PS1 0x80068418): its target panel for a
    /// panel-targeted action, otherwise the panel of the unit it targets.
    /// </summary>
    public byte* TargetedTile(byte* unit)
    {
        if (unit[Battle.UnitTargetType] == 5)
            return TileAt(*(short*)(unit + Battle.UnitTargetX), *(short*)(unit + Battle.UnitTargetY),
                          *(short*)(unit + Battle.UnitTargetLevel));
        var target = _getUnit(unit[Battle.UnitTargetIndex]);
        return target == null ? null : TileUnder(target);
    }
}
