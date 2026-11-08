using SonicScrewdriver.Input;

namespace SonicScrewdriver.Bindings;

/// <summary>A modifier combination bound to a target; applies to every media key.</summary>
public sealed record KeyBinding(Modifiers Modifiers, Target Target, int StepPercent = 5);
