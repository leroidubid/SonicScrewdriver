namespace SonicScrewdriver.Settings;

/// <summary>"This app always plays on this output device": the exe, the device's MMDevice ID, and its name for display.</summary>
public sealed record RoutingRule(string ExeName, string DeviceId, string DeviceName = "");
