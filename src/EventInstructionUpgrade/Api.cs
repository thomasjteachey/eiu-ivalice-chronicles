using EventInstructionUpgrade.Interfaces;

namespace EventInstructionUpgrade;

/// <summary>What mods built on the upgrade use (<see cref="IEventInstructions"/>), over <see cref="EventHooks"/>.</summary>
internal sealed unsafe class Api : IEventInstructions
{
    public bool Installed => EventHooks.Installed;
    public byte[] Marker => [.. EventHooks.Magic];
    public int MaxCode => EventHooks.MaxCode;

    public void MapMemory(uint address, int length, nint place)
    {
        if (EventHooks.Memory is not { } memory)
            throw new InvalidOperationException("the Event Instruction Upgrade isn't installed");
        memory.Map(address, length, (byte*)place);
    }

    public void AddVariables(Func<int, bool> holds, Func<int, int> get, Action<int, int> set) =>
        EventHooks.ExtraVariables.Add((holds, get, set));

    public int GetVariable(int id) => EventHooks.GetWord(id);
    public void SetVariable(int id, int value) => EventHooks.SetWord(id, value);
    public nint Words => (nint)EventHooks.Words;

    public void AddRoutine(uint address, Action routine)
    {
        if (EventHooks.Script is not { } script)
            throw new InvalidOperationException("the Event Instruction Upgrade isn't installed");
        script.Natives[address] = routine;
    }

    public void SetConditionals(byte[] conditions)
    {
        if (EventHooks.Script is not { } script)
            throw new InvalidOperationException("the Event Instruction Upgrade isn't installed");
        script.Presets = conditions;
    }

    public void AddStepObserver(Action<nint> observer) => EventHooks.StepObservers.Add(observer);
    public void OnGraphicWritten(Action<uint> listener) => EventHooks.GraphicListeners.Add(listener);
    public void OnUnitPreset(Action<int> listener) => CreateUnit.PresetChosen += listener;

    public void OnMessage(Action<string> note, Action<string> error)
    {
        EventHooks.NoteListeners.Add(note);
        EventHooks.ErrorListeners.Add(error);
    }

    public int Next(nint code, int offset) => EventScript.Next((byte*)code, offset);
    public int PhaseBlock(nint code, int offset, int size) => EventHooks.PhaseBlock((byte*)code, offset, size);

    public nint RunningCode => EventHooks.RunningCode;
    public int LastOffset => EventHooks.LastPc;
    public int LastOpcode => EventHooks.LastOp;

    public bool TraceSteps
    {
        get => EventHooks.TraceSteps;
        set => EventHooks.TraceSteps = value;
    }
}
