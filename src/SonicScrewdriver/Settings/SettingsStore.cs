using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonicScrewdriver.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SonicScrewdriver", "settings.json");

    public bool RecoveredFromCorruption { get; private set; }

    public AppSettings Load()
    {
        RecoveredFromCorruption = false;
        if (!File.Exists(_path)) return AppSettings.CreateDefault();
        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked (antivirus, sync client) or unreadable: run with defaults, leave the file alone.
            return AppSettings.CreateDefault();
        }
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (s is null) throw new JsonException("empty");
            s.Bindings ??= AppSettings.CreateDefault().Bindings;
            s.Bindings = Sanitize(s.Bindings);
            s.CycleDeviceIds ??= new List<string>();
            s.AutoPrograms = SanitizePrograms(s.AutoPrograms);
            s.CompetitiveHotkey =Input.Hotkey.TryParse(s.CompetitiveHotkey, out var hotkey) ? hotkey.ToString() : null;
            return s;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            try
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
                File.Delete(_path);
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException) { }
            RecoveredFromCorruption = true;
            return AppSettings.CreateDefault();
        }
    }

    /// <summary>
    /// Makes hand-edited rows safe: normalizes exe names, clamps the step to 1–50 %,
    /// drops rows without a modifier or target, and keeps the first of duplicate modifier sets.
    /// </summary>
    private static List<Bindings.KeyBinding> Sanitize(List<Bindings.KeyBinding> rows)
    {
        var seen = new HashSet<Input.Modifiers>();
        var result = new List<Bindings.KeyBinding>();
        foreach (var b in rows)
        {
            if (b?.Target is null || b.Modifiers == Input.Modifiers.None) continue;
            var target = b.Target.Kind switch
            {
                Bindings.TargetKind.Process => Bindings.Target.Process(b.Target.ExeName ?? ""),
                Bindings.TargetKind.OutputDevice => Bindings.Target.OutputDevice,
                _ => Bindings.Target.Foreground,
            };
            if (target.Kind == Bindings.TargetKind.Process && string.IsNullOrEmpty(target.ExeName)) continue;
            if (!seen.Add(b.Modifiers)) continue;
            result.Add(b with { Target = target, StepPercent = Math.Clamp(b.StepPercent, 1, 50) });
        }
        return result;
    }

    /// <summary>Normalizes exe names, drops empty entries and keeps the first of duplicates.</summary>
    internal static List<AutoProgram> SanitizePrograms(List<AutoProgram>? programs)
    {
        var seen = new HashSet<string>();
        var result = new List<AutoProgram>();
        foreach (var p in programs ?? new List<AutoProgram>())
        {
            var exe = Bindings.Target.NormalizeExe(p?.ExeName ?? "");
            if (exe.Length > 0 && seen.Add(exe)) result.Add(new AutoProgram(exe, p!.Enabled));
        }
        return result;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, _path, overwrite: true);
    }
}
