using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Mod.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace IvaliceCore;

/// <summary>The hooks a mod's startup gets: Reloaded.Hooks from the shared hooks mod, or one that says it's missing.</summary>
internal static class Hooks
{
    public static IHooks From(IModLoader loader)
    {
        IReloadedHooks? hooks = null;
        loader.GetController<IReloadedHooks>()?.TryGetTarget(out hooks);
        return hooks is null ? new MissingHooks() : new ReloadedHooks(hooks);
    }

    /// <summary>Reloaded.Hooks from the shared hooks mod. Keeps the hooks alive for the game's lifetime.</summary>
    private sealed unsafe class ReloadedHooks(IReloadedHooks hooks) : IHooks
    {
        private readonly List<IHook<NativeAction>> _active = [];

        public nint Hook(nint function, nint detour, string name)
        {
            var hook = hooks.CreateHook<NativeAction>((void*)detour, function).Activate();
            _active.Add(hook);
            return hook.OriginalFunctionAddress;
        }
    }

    private sealed class MissingHooks : IHooks
    {
        public nint Hook(nint function, nint detour, string name) =>
            throw new InvalidOperationException("Reloaded-II's shared hooks mod (reloaded.sharedlib.hooks) isn't loaded");
    }

    /// <summary>The hooked functions' shape as far as Reloaded.Hooks needs it; detours are [UnmanagedCallersOnly].</summary>
    [Function(CallingConventions.Microsoft)]
    private delegate void NativeAction();
}
