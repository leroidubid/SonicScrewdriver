using SonicScrewdriver.Input;

namespace SonicScrewdriver.Settings;

/// <summary>One physical key that acts as a media key, stored by key name ("F11").</summary>
public sealed record KeyRemap(MediaKey Action, string Key);
