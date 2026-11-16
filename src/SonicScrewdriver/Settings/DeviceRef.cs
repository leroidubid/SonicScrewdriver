namespace SonicScrewdriver.Settings;

/// <summary>An output device in an ordered list: its MMDevice ID and its name for display.</summary>
public sealed record DeviceRef(string Id, string Name = "");
