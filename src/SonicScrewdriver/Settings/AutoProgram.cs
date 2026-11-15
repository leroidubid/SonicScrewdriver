namespace SonicScrewdriver.Settings;

/// <summary>A program of the automatic competitive-mode list: its exe name, and whether it currently triggers the mode.</summary>
public sealed record AutoProgram(string ExeName, bool Enabled = true);
