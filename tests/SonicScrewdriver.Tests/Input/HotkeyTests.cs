using System.IO;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Input;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F9", Modifiers.Ctrl | Modifiers.Alt, 0x78)]
    [InlineData("shift+win+c", Modifiers.Shift | Modifiers.Win, 'C')]
    [InlineData(" Ctrl + PageUp ", Modifiers.Ctrl, 0x21)]
    public void TryParse_accepts_modifiers_plus_key(string text, Modifiers mods, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.Equal(new Hotkey(mods, vk), h);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F9")]            // no modifier
    [InlineData("Ctrl")]          // no key
    [InlineData("Ctrl+Ctrl+F9")]  // duplicate modifier
    [InlineData("Ctrl+Banana")]   // unknown key
    [InlineData("Foo+F9")]        // unknown modifier
    public void TryParse_rejects_invalid(string? text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void ToString_is_canonical_and_roundtrips()
    {
        Assert.True(Hotkey.TryParse("win+shift+alt+ctrl+f9", out var h));
        Assert.Equal("Ctrl+Alt+Shift+Win+F9", h.ToString());
        Assert.True(Hotkey.TryParse(h.ToString(), out var again));
        Assert.Equal(h, again);
    }

    [Fact]
    public void Remappable_keys_exclude_typing_and_modifier_keys()
    {
        foreach (var name in new[] { "F1", "F24", "Pause", "ScrollLock", "Insert", "Home", "End", "PageUp", "PageDown", "Numpad0", "NumpadAdd" })
        {
            Assert.True(KeyNames.TryGetVirtualKey(name, out var vk), name);
            Assert.True(KeyNames.IsRemappable(vk), name);
        }
        foreach (var name in new[] { "A", "5", "Space", "Up", "Delete" })
        {
            Assert.True(KeyNames.TryGetVirtualKey(name, out var vk), name);
            Assert.False(KeyNames.IsRemappable(vk), name);
        }
        Assert.False(KeyNames.IsRemappable(0x10)); // Shift
        Assert.False(KeyNames.IsRemappable(0x0D)); // Enter
    }
}

public class CompetitiveHotkeySettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-hotkey-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "settings.json");

    public CompetitiveHotkeySettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_to_none_and_roundtrips_in_canonical_form()
    {
        Assert.Null(AppSettings.CreateDefault().CompetitiveHotkey);
        new SettingsStore(File_).Save(new AppSettings { CompetitiveHotkey = "ctrl+alt+f9" });
        Assert.Equal("Ctrl+Alt+F9", new SettingsStore(File_).Load().CompetitiveHotkey);
    }

    [Fact]
    public void Invalid_hand_edited_value_is_dropped_on_load()
    {
        File.WriteAllText(File_, "{\"CompetitiveHotkey\":\"F9\"}");
        Assert.Null(new SettingsStore(File_).Load().CompetitiveHotkey);
    }

    [Fact]
    public void Validate_flags_a_hotkey_without_modifier()
    {
        Assert.Contains(new AppSettings { CompetitiveHotkey = "F9" }.Validate(), e => e.Contains("Competitive mode shortcut"));
        Assert.DoesNotContain(new AppSettings { CompetitiveHotkey = "Ctrl+F9" }.Validate(), e => e.Contains("Competitive mode shortcut"));
        Assert.DoesNotContain(new AppSettings { CompetitiveHotkey = null }.Validate(), e => e.Contains("Competitive mode shortcut"));
    }
}
