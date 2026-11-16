namespace SonicScrewdriver.Settings;

/// <summary>A maximum volume (1-100 %) for one app, by exe name.</summary>
public sealed record AppLimit(string ExeName, int Percent);
