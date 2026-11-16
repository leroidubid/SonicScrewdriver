using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Automation;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Settings;

public class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public ProfileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static VolumeProfile Night(int? master = null) => new("Game night",
        new() { new("discord.exe", 80), new("spotify.exe", 30), new("chrome.exe", 10) }, master);

    [Fact]
    public void Apply_sets_playing_apps_skips_the_others_and_unmutes()
    {
        var audio = new FakeAudio();
        audio.Apps["discord.exe"] = (0.2f, true);
        audio.Apps["spotify.exe"] = (1f, false);
        var r = ProfileApplier.Apply(Night(), audio);
        Assert.Equal(new ProfileResult(2, 1, false), r);
        Assert.Equal((0.8f, false), audio.Apps["discord.exe"]);
        Assert.Equal((0.3f, false), audio.Apps["spotify.exe"]);
    }

    [Fact]
    public void Apply_sets_the_master_volume_when_the_profile_has_one()
    {
        var audio = new FakeAudio();
        var r = ProfileApplier.Apply(Night(master: 35), audio);
        Assert.True(r.MasterSet);
        Assert.Equal(0.35f, audio.MasterVolume);
        ProfileApplier.Apply(Night(), audio);
        Assert.Equal(0.35f, audio.MasterVolume); // no master in the profile: untouched
    }

    [Fact]
    public void Apply_to_zero_keeps_the_mute_state_and_respects_the_cap()
    {
        var audio = new FakeAudio();
        audio.Apps["a.exe"] = (0.5f, true);
        audio.Apps["b.exe"] = (0.5f, false);
        audio.Limits.Set(new[] { new AppLimit("b.exe", 40) });
        ProfileApplier.Apply(new VolumeProfile("p", new() { new("a.exe", 0), new("b.exe", 90) }), audio);
        Assert.Equal((0f, true), audio.Apps["a.exe"]);
        Assert.Equal(0.4f, audio.Apps["b.exe"].Vol);
    }

    [Fact]
    public void Sanitize_cleans_hand_edited_profiles()
    {
        var seen = new HashSet<Hotkey> { new(Modifiers.Ctrl | Modifiers.Alt, 0x78) }; // Ctrl+Alt+F9 taken
        var raw = new List<VolumeProfile?>
        {
            new("  Game night  ", new() { new(" Discord ", 500), new("discord.exe", 10), new("", 5), new("x", -4) }, 250, "ctrl+alt+f9", " Game "),
            new("game NIGHT", new(), null, null, null),   // duplicate name
            new("   ", new(), null, null, null),          // empty name
            null,
            new(new string('n', 100), null!, null, "shift+alt+p", null),
        };
        var list = VolumeProfile.Sanitize(raw, seen);
        Assert.Equal(2, list.Count);
        Assert.Equal("Game night", list[0].Name);
        Assert.Equal(new[] { new ProfileApp("discord.exe", 100), new ProfileApp("x.exe", 0) }, list[0].Apps);
        Assert.Equal(100, list[0].MasterVolume);
        Assert.Null(list[0].Hotkey);                      // already taken by another shortcut
        Assert.Equal("game.exe", list[0].OnProgramStart);
        Assert.Equal(VolumeProfile.MaxNameLength, list[1].Name.Length);
        Assert.Empty(list[1].Apps);
        Assert.Equal("Alt+Shift+P", list[1].Hotkey);
    }

    [Fact]
    public void Profiles_survive_a_save_and_load_and_a_hand_edited_file()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Profiles = { Night(40) with { Hotkey = "Ctrl+Alt+G", OnProgramStart = "game.exe" } } });
        var loaded = store.Load().Profiles;
        Assert.Single(loaded);
        Assert.Equal("Ctrl+Alt+G", loaded[0].Hotkey);
        Assert.Equal(3, loaded[0].Apps.Count);
        Assert.Equal(40, loaded[0].MasterVolume);

        File.WriteAllText(path, "{ \"Profiles\": [ { \"Name\": \"Only name\" }, { \"Apps\": [ { \"ExeName\": \"a\", \"Volume\": 5 } ] }, 7 ] }");
        var weird = new SettingsStore(path);
        // A malformed entry (wrong type) is a corrupt file: defaults, and a backup.
        Assert.Empty(weird.Load().Profiles);
        Assert.True(weird.RecoveredFromCorruption);

        File.WriteAllText(path, "{ \"Profiles\": [ { \"Name\": \"Only name\" }, { \"Apps\": [ { \"ExeName\": \"a\", \"Volume\": 5 } ] } ] }");
        var ok = new SettingsStore(path).Load().Profiles;
        Assert.Equal("Only name", Assert.Single(ok).Name);
        Assert.Empty(ok[0].Apps);
    }

    [Fact]
    public void Validate_reports_every_problem()
    {
        Assert.Empty(new AppSettings { Profiles = { Night(50) with { Hotkey = "Ctrl+Alt+G" } } }.Validate());
        Assert.NotEmpty(new AppSettings { Profiles = { new("", new()) } }.Validate());
        Assert.NotEmpty(new AppSettings { Profiles = { Night(), Night() } }.Validate());
        Assert.NotEmpty(new AppSettings { Profiles = { Night(150) } }.Validate());
        Assert.NotEmpty(new AppSettings { Profiles = { new("p", new() { new("a", 101) }) } }.Validate());
        Assert.NotEmpty(new AppSettings { Profiles = { new("p", new() { new("a", 1), new("A.exe", 2) }) } }.Validate());
        Assert.NotEmpty(new AppSettings { Profiles = { new("p", new(), null, "Nope") } }.Validate());
        Assert.NotEmpty(new AppSettings { MuteAllHotkey = "Ctrl+Alt+G", Profiles = { new("p", new(), null, "ctrl+alt+g") } }.Validate());
    }

    [Fact]
    public void Find_is_case_insensitive_and_trims()
    {
        var list = new List<VolumeProfile> { Night() };
        Assert.NotNull(VolumeProfile.Find(list, " game NIGHT "));
        Assert.Null(VolumeProfile.Find(list, "other"));
    }

    [Fact]
    public void Trigger_fires_once_when_the_program_appears_and_not_for_one_already_running()
    {
        var p = Night() with { OnProgramStart = "game.exe" };
        var list = new List<VolumeProfile> { p, Night() with { Name = "No trigger" } };
        var t = new ProfileTriggers();
        Assert.Empty(t.Started(list, new HashSet<string> { "game.exe" }));            // baseline: already running
        Assert.Empty(t.Started(list, new HashSet<string> { "game.exe" }));
        Assert.Empty(t.Started(list, new HashSet<string>()));                         // exited
        Assert.Equal(new[] { p }, t.Started(list, new HashSet<string> { "game.exe" })); // started again
        Assert.Empty(t.Started(list, new HashSet<string> { "game.exe" }));
        t.Reset();
        Assert.Empty(t.Started(list, new HashSet<string> { "game.exe" }));            // new baseline
        Assert.True(ProfileTriggers.Any(list));
        Assert.False(ProfileTriggers.Any(new[] { Night() }));
    }
}
