namespace EventInstructionUpgrade.Interfaces;

/// <summary>
/// The Event Instruction Upgrade (Xifanie's PS1 hack, as The Lion War ReMixed uses it) run in The Ivalice
/// Chronicles: control flow (If, Switch, subroutines, GoTo), 32-bit variables, the PS1's memory through
/// LoadAddress and SaveAddress, AlterParameter, GenerateUnit and the rest. It runs an event's code when the code
/// starts with <see cref="Marker"/>; the remaster runs the instructions both games share.
/// <para>A mod built on it adds what its own events need: native routines for Routine (0xCF), its battle event
/// conditions for LoadConditionals (0xCE), PS1 memory the upgrade doesn't know, and variables kept elsewhere.</para>
/// </summary>
public interface IEventInstructions
{
    /// <summary>Whether the upgrade's hooks are in the game; without them nothing here does anything.</summary>
    bool Installed { get; }

    /// <summary>What an event's code starts with for the upgrade to run it (LABEL 0x2C, LABEL 0x3C).</summary>
    byte[] Marker { get; }

    /// <summary>The bytes an event's code may take, after the u32 the remaster puts in front of it.</summary>
    int MaxCode { get; }

    /// <summary>PS1 <paramref name="address"/> and the <paramref name="length"/> bytes after it live at
    /// <paramref name="place"/> (for LoadAddress and SaveAddress). A later map of the same bytes wins.</summary>
    void MapMemory(uint address, int length, nint place);

    /// <summary>Event variables kept outside the remaster's array: <paramref name="holds"/> says which ids.</summary>
    void AddVariables(Func<int, bool> holds, Func<int, int> get, Action<int, int> set);

    /// <summary>An event variable as the events see it.</summary>
    int GetVariable(int id);

    /// <summary>Sets an event variable as an event would.</summary>
    void SetVariable(int id, int value);

    /// <summary>The upgrade's words: the variables 0xB00–0xB7F and the text values (0x9A–0x9E), 0x100 words at PS1
    /// 0x80165EF4.</summary>
    nint Words { get; }

    /// <summary>Routine (0xCF): the native routine at PS1 <paramref name="address"/>, in C#.</summary>
    void AddRoutine(uint address, Action routine);

    /// <summary>LoadConditionals (0xCE): the battle event conditions per event number, 0x220 bytes each (0x20 of set
    /// offsets, then 0x200 of records).</summary>
    void SetConditionals(byte[] conditions);

    /// <summary>Called with each instruction's address before it runs, in the upgrade's events.</summary>
    void AddStepObserver(Action<nint> observer);

    /// <summary>Called with a battle unit's index when an event writes its graphic (PS1 +0x15F).</summary>
    void OnGraphicWritten(Action<uint> listener);

    /// <summary>GenerateUnit (0xE0): told the record's +0x25 before the unit is made, and −1 after.</summary>
    void OnUnitPreset(Action<int> listener);

    /// <summary>The upgrade's notes (what it passed over, what it did) and its errors.</summary>
    void OnMessage(Action<string> note, Action<string> error);

    /// <summary>The instruction after the one at <paramref name="offset"/> in an upgrade event's code.</summary>
    int Next(nint code, int offset);

    /// <summary>0xD1 n in an upgrade event: the offset after the next 0xD2 n, or the END's.</summary>
    int PhaseBlock(nint code, int offset, int size);

    /// <summary>For a test harness: the running event's code, the last instruction's offset and opcode, and whether
    /// each instruction is logged.</summary>
    nint RunningCode { get; }
    int LastOffset { get; }
    int LastOpcode { get; }
    bool TraceSteps { get; set; }
}
