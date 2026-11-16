using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Settings;

public class FreeHotkeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "settings.json");

    public FreeHotkeyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static AppSettings With(params FreeHotkey[] hotkeys) => new() { FreeHotkeys = hotkeys.ToList() };

    [Fact]
    public void Valid_free_hotkeys_pass_validation()
    {
        var s = With(new FreeHotkey("Ctrl+Alt+F9", MediaKey.VolumeUp, Target.Foreground, 5),
                     new FreeHotkey("Win+Shift+Backquote", MediaKey.Mute, Target.Process("spotify"), 10));
        Assert.Empty(s.Validate());
    }

    [Theory]
    [InlineData("F9")]            // no modifier
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nope")]
    [InlineData("")]
    public void Invalid_chord_is_rejected(string chord) =>
        Assert.NotEmpty(With(new FreeHotkey(chord, MediaKey.Mute, Target.Foreground)).Validate());

    [Fact]
    public void Duplicate_chord_and_chord_of_the_mode_shortcut_are_rejected()
    {
        var dup = With(new FreeHotkey("Ctrl+F9", MediaKey.Mute, Target.Foreground), new FreeHotkey("ctrl+f9", MediaKey.Next, Target.Foreground));
        Assert.Contains(dup.Validate(), e => e.Contains("already used"));
        var mode = With(new FreeHotkey("Ctrl+Alt+F9", MediaKey.Mute, Target.Foreground));
        mode.CompetitiveHotkey = "Alt+Ctrl+F9";
        Assert.Contains(mode.Validate(), e => e.Contains("already used"));
    }

    [Fact]
    public void Step_and_target_are_validated()
    {
        Assert.NotEmpty(With(new FreeHotkey("Ctrl+F9", MediaKey.VolumeUp, Target.Foreground, 0)).Validate());
        Assert.NotEmpty(With(new FreeHotkey("Ctrl+F9", MediaKey.VolumeUp, Target.Foreground, 51)).Validate());
        Assert.NotEmpty(With(new FreeHotkey("Ctrl+F9", MediaKey.VolumeUp, new Target(TargetKind.Process, " "))).Validate());
    }

    [Fact]
    public void Chord_maps_to_its_action_and_binding()
    {
        var map = FreeHotkeyMap.From(new[]
        {
            new FreeHotkey("Ctrl+Alt+F9", MediaKey.VolumeUp, Target.Process("spotify"), 7),
            new FreeHotkey("Shift+Semicolon", MediaKey.Next, Target.OutputDevice),
        });
        Assert.Equal(2, map.Entries.Count);
        var e = map.Find(new Hotkey(Modifiers.Ctrl | Modifiers.Alt, 0x78))!;
        Assert.Equal(MediaKey.VolumeUp, e.Action);
        Assert.Equal(new KeyBinding(Modifiers.Ctrl | Modifiers.Alt, Target.Process("spotify.exe"), 7), e.Binding);
        Assert.Equal(MediaKey.Next, map.Find(new Hotkey(Modifiers.Shift, 0xBA))!.Action);
        Assert.Null(map.Find(new Hotkey(Modifiers.Ctrl, 0x78)));
    }

    [Fact]
    public void Map_skips_invalid_chords_and_keeps_the_first_duplicate()
    {
        var map = FreeHotkeyMap.From(new[]
        {
            new FreeHotkey("F9", MediaKey.Mute, Target.Foreground),
            new FreeHotkey("Ctrl+F9", MediaKey.Mute, Target.Foreground),
            new FreeHotkey("Ctrl+F9", MediaKey.Next, Target.Foreground),
        });
        Assert.Equal(MediaKey.Mute, Assert.Single(map.Entries).Action);
    }

    [Fact]
    public void Summary_lists_free_hotkeys()
    {
        var lines = ShortcutSummary.FreeLines(new[]
        {
            new FreeHotkey("Ctrl+F9", MediaKey.VolumeDown, Target.Process("spotify")),
            new FreeHotkey("nonsense", MediaKey.Mute, Target.Foreground),
        });
        Assert.Equal("Ctrl+F9 Vol− → spotify.exe", Assert.Single(lines));
    }

    [Fact]
    public void Hand_edited_file_is_sanitized_and_roundtrips()
    {
        File.WriteAllText(File_, """
        {
          "CompetitiveHotkey": "Ctrl+Alt+F1",
          "FreeHotkeys": [
            { "Chord": "alt+ctrl+f9", "Action": "VolumeUp", "Target": { "Kind": "Process", "ExeName": "Spotify" }, "StepPercent": 500 },
            { "Chord": "Ctrl+Alt+F9", "Action": "Mute", "Target": { "Kind": "Foreground" }, "StepPercent": 5 },
            { "Chord": "Ctrl+Alt+F1", "Action": "Mute", "Target": { "Kind": "Foreground" }, "StepPercent": 5 },
            { "Chord": "F5", "Action": "Mute", "Target": { "Kind": "Foreground" }, "StepPercent": 5 },
            { "Chord": "Ctrl+F5", "Action": "Mute", "Target": { "Kind": "Process", "ExeName": " " }, "StepPercent": 5 },
            { "Chord": "Ctrl+F6", "Action": "Mute", "Target": null }
          ]
        }
        """);
        var s = new SettingsStore(File_).Load();
        var f = Assert.Single(s.FreeHotkeys);
        Assert.Equal("Ctrl+Alt+F9", f.Chord);
        Assert.Equal(50, f.StepPercent);
        Assert.Equal("spotify.exe", f.Target.ExeName);

        new SettingsStore(File_).Save(s);
        var again = new SettingsStore(File_).Load();
        Assert.Equal(s.FreeHotkeys, again.FreeHotkeys);
    }

    [Fact]
    public void Missing_lists_load_as_empty()
    {
        File.WriteAllText(File_, "{ \"FreeHotkeys\": null }");
        var s = new SettingsStore(File_).Load();
        Assert.Empty(s.FreeHotkeys);
    }
}
