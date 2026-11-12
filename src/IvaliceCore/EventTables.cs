namespace IvaliceCore;

/// <summary>The remaster's battle event tables that events and scenarios read and write.</summary>
internal static unsafe class EventTables
{
    /// <summary>The battle event condition check (0x2391C4): its sets' offsets (0xD40040) and the records (0xD3F550),
    /// image-relative in movsx eax, word [rsi + rbx * 2 + sets] and lea rdi, [rsi + records].</summary>
    internal const string ConditionsPattern =
        "C6 40 6C 00 0F BF 84 5E ?? ?? ?? ?? 48 85 DB 74 04 85 C0 74 ?? 85 C0 79 02 FF C0 D1 F8 48 8D BE";

    internal static (nint Sets, nint Records) ConditionTables(GameImage image)
    {
        var at = image.FindUnique(ConditionsPattern);
        return (image.ImageStart + *(int*)(at + 8), image.ImageStart + *(int*)(at + 32));
    }

    /// <summary>The world setup filling the per-event map settings from its scenario records (0x2FBB05): the table's
    /// image-relative address in its store, mov [r15 + rax * 2 + table], r8w, with r15 the image's start.</summary>
    internal const string MapSettingsPattern = "41 0F B6 51 01 0F AF D1 49 83 C1 18 66 44 0B C2 66 45 89 84 47";

    /// <summary>The per-event map-settings words (PS1 0x8004E5D0, the remaster's 0xD3F950: what a scenario hands on to,
    /// read at its end), which the world setup fills from the scenario records (0x2FBB15).</summary>
    internal static ushort* MapSettings(GameImage image) =>
        (ushort*)(image.ImageStart + *(int*)(image.FindUnique(MapSettingsPattern) + 21));

    /// <summary>The battle setup's sum of the enemies' levels (a word; PS1 0x80193844) and their highest (a byte; PS1
    /// 0x80193920), in its loop at 0x2716C5 (the PS1 adds them as units are built, 0x80180DC4).</summary>
    internal const string EnemyLevelsPattern = "0F B6 4B 29 66 01 0D ?? ?? ?? ?? 3A 0D ?? ?? ?? ?? 76 ?? 88 0D";
}
