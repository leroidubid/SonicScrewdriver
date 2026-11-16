using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Settings;

public class PinHideSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "settings.json");

    public PinHideSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Hand_edited_lists_are_sanitized_and_roundtrip()
    {
        File.WriteAllText(File_, """
        {
          "PinnedApps": ["Spotify", "spotify.exe", "", "Chrome", "hidden"],
          "HiddenApps": ["HIDDEN.EXE", "hidden", " "]
        }
        """);
        var s = new SettingsStore(File_).Load();
        Assert.Equal(new[] { "spotify.exe", "chrome.exe" }, s.PinnedApps);   // a hidden app is not pinned
        Assert.Equal(new[] { "hidden.exe" }, s.HiddenApps);
        new SettingsStore(File_).Save(s);
        var again = new SettingsStore(File_).Load();
        Assert.Equal(s.PinnedApps, again.PinnedApps);
        Assert.Equal(s.HiddenApps, again.HiddenApps);
    }

    [Fact]
    public void Null_lists_load_as_empty()
    {
        File.WriteAllText(File_, "{ \"PinnedApps\": null, \"HiddenApps\": null }");
        var s = new SettingsStore(File_).Load();
        Assert.Empty(s.PinnedApps);
        Assert.Empty(s.HiddenApps);
    }
}

public class AppArrangementTests
{
    private static List<string> Arrange(string[] apps, string[] pinned, string[] hidden) =>
        AppArrangement.Arrange(apps, a => a, pinned, hidden);

    [Fact]
    public void Without_preferences_the_order_is_kept() =>
        Assert.Equal(new[] { "a.exe", "b.exe", "c.exe" }, Arrange(new[] { "a.exe", "b.exe", "c.exe" }, Array.Empty<string>(), Array.Empty<string>()));

    [Fact]
    public void Pinned_apps_come_first_in_pin_order()
    {
        var result = Arrange(new[] { "a.exe", "b.exe", "c.exe", "d.exe" }, new[] { "d.exe", "b.exe" }, Array.Empty<string>());
        Assert.Equal(new[] { "d.exe", "b.exe", "a.exe", "c.exe" }, result);
    }

    [Fact]
    public void Hidden_apps_are_dropped_and_matching_ignores_case()
    {
        var result = Arrange(new[] { "A.exe", "b.exe", "c.exe" }, new[] { "C.EXE" }, new[] { "a.exe" });
        Assert.Equal(new[] { "c.exe", "b.exe" }, result);
    }

    [Fact]
    public void Pinned_apps_that_are_not_playing_are_ignored_and_a_hidden_pin_stays_hidden()
    {
        var result = Arrange(new[] { "a.exe", "b.exe" }, new[] { "gone.exe", "b.exe", "a.exe" }, new[] { "a.exe" });
        Assert.Equal(new[] { "b.exe" }, result);
    }
}
