using System.IO;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    private string PathFile => System.IO.Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Load_missing_file_returns_default()
    {
        var s = new SettingsStore(PathFile).Load();
        var b = Assert.Single(s.Bindings);
        Assert.Equal(Modifiers.Ctrl, b.Modifiers);
        Assert.Equal(TargetKind.Foreground, b.Target.Kind);
        Assert.Equal(5, b.StepPercent);
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var store = new SettingsStore(PathFile);
        var s = new AppSettings
        {
            Bindings = { new KeyBinding(Modifiers.Shift, Target.Process("Spotify"), 2) },
            StartWithWindows = true,
        };
        store.Save(s);
        var loaded = new SettingsStore(PathFile).Load();
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(new KeyBinding(Modifiers.Shift, Target.Process("spotify.exe"), 2), Assert.Single(loaded.Bindings));
    }

    [Fact]
    public void CompetitiveMode_defaults_to_off_and_roundtrips()
    {
        Assert.False(AppSettings.CreateDefault().CompetitiveMode);
        new SettingsStore(PathFile).Save(new AppSettings { CompetitiveMode = true });
        Assert.True(new SettingsStore(PathFile).Load().CompetitiveMode);
    }

    [Fact]
    public void Load_corrupt_file_backs_up_and_returns_default()
    {
        File.WriteAllText(PathFile, "{not json");
        var store = new SettingsStore(PathFile);
        var s = store.Load();
        Assert.Single(s.Bindings);
        Assert.True(File.Exists(PathFile + ".bak"));
        Assert.True(store.RecoveredFromCorruption);
    }

    [Fact]
    public void Load_locked_file_returns_default_without_throwing()
    {
        File.WriteAllText(PathFile, "{}");
        using var lockHandle = new FileStream(PathFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var store = new SettingsStore(PathFile);
        Assert.Single(store.Load().Bindings);
        Assert.False(store.RecoveredFromCorruption);
    }

    [Fact]
    public void A_1_2_0_ducking_and_streamer_section_loads_unchanged_and_is_written_with_the_same_names()
    {
        File.WriteAllText(PathFile, """
        {
          "Ducking": { "Enabled": true, "DuckToPercent": 25, "ThresholdPercent": 6, "AttackMs": 120, "ReleaseMs": 1800,
            "Apps": [ { "ExeName": "discord.exe", "Voice": true, "Ducked": false }, { "ExeName": "spotify.exe", "Voice": false, "Ducked": true } ] },
          "Streamer": { "Enabled": true, "DuckToPercent": 40, "ThresholdPercent": 8, "AttackMs": 200, "ReleaseMs": 900,
            "Apps": [ { "ExeName": "spotify.exe", "Lowered": true }, { "ExeName": "chrome.exe", "Lowered": false } ] }
        }
        """);
        var store = new SettingsStore(PathFile);
        var s = store.Load();
        Assert.Equal(new DuckingSettings(true, 25, 6, 120, 1800, new() { new("discord.exe", true, false), new("spotify.exe", false, true) }).AppList, s.Ducking.AppList);
        Assert.Equal((true, 25, 6, 120, 1800), (s.Ducking.Enabled, s.Ducking.DuckToPercent, s.Ducking.ThresholdPercent, s.Ducking.AttackMs, s.Ducking.ReleaseMs));
        Assert.Equal((true, 40, 8, 200, 900), (s.Streamer.Enabled, s.Streamer.DuckToPercent, s.Streamer.ThresholdPercent, s.Streamer.AttackMs, s.Streamer.ReleaseMs));
        Assert.Equal(new[] { new StreamApp("spotify.exe", true), new StreamApp("chrome.exe", false) }, s.Streamer.AppList);

        store.Save(s);
        var text = File.ReadAllText(PathFile);
        foreach (var name in new[] { "\"DuckToPercent\"", "\"ThresholdPercent\"", "\"AttackMs\"", "\"ReleaseMs\"", "\"Apps\"", "\"Voice\"", "\"Ducked\"", "\"Lowered\"" })
            Assert.Contains(name, text);
    }

    [Fact]
    public void Load_sanitizes_hand_edited_rows()
    {
        File.WriteAllText(PathFile, """
        {
          "Bindings": [
            { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "Spotify" }, "StepPercent": 0 },
            { "Modifiers": "Shift", "Target": { "Kind": "Foreground", "ExeName": null }, "StepPercent": 5 },
            { "Modifiers": "None",  "Target": { "Kind": "Foreground", "ExeName": null }, "StepPercent": 5 },
            { "Modifiers": "Alt",   "Target": { "Kind": "Process", "ExeName": "" }, "StepPercent": 5 },
            { "Modifiers": "Ctrl",  "Target": { "Kind": "Foreground", "ExeName": null }, "StepPercent": 99 }
          ]
        }
        """);
        var s = new SettingsStore(PathFile).Load();
        Assert.Equal(
            new[]
            {
                new KeyBinding(Modifiers.Shift, Target.Process("spotify.exe"), 1),  // step clamped, first Shift wins
                new KeyBinding(Modifiers.Ctrl, Target.Foreground, 50),              // step clamped
            },
            s.Bindings);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Load_null_bindings_returns_default_list()
    {
        File.WriteAllText(PathFile, "{\"Bindings\": null}");
        Assert.NotNull(new SettingsStore(PathFile).Load().Bindings);
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Validate_default_is_clean() => Assert.Empty(AppSettings.CreateDefault().Validate());

    [Fact]
    public void Validate_flags_duplicate_modifiers()
    {
        var s = new AppSettings
        {
            Bindings =
            {
                new KeyBinding(Modifiers.Shift, Target.Foreground),
                new KeyBinding(Modifiers.Shift, Target.Process("a")),
            },
        };
        Assert.NotEmpty(s.Validate());
    }

    [Theory]
    [InlineData(Modifiers.None, "a", 5)]
    [InlineData(Modifiers.Alt, "a", 0)]
    [InlineData(Modifiers.Alt, "a", 51)]
    [InlineData(Modifiers.Alt, "  ", 5)]
    public void Validate_flags_invalid_binding(Modifiers mods, string exe, int step)
    {
        var s = new AppSettings { Bindings = { new KeyBinding(mods, Target.Process(exe), step) } };
        Assert.NotEmpty(s.Validate());
    }
}
