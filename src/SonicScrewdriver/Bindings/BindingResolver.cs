using SonicScrewdriver.Input;

namespace SonicScrewdriver.Bindings;

public sealed class BindingResolver
{
    private readonly IReadOnlyList<KeyBinding> _bindings;

    public BindingResolver(IReadOnlyList<KeyBinding> bindings) => _bindings = bindings;

    /// <summary>Exact modifier match; no modifier never matches (bare keys keep their default behavior).</summary>
    public KeyBinding? Resolve(Modifiers mods)
    {
        if (mods == Modifiers.None) return null;
        foreach (var b in _bindings)
            if (b.Modifiers == mods) return b;
        return null;
    }
}
