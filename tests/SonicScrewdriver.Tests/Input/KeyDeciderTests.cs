using System.IO;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Input;

public class KeyDeciderTests
{
    private const int F10 = 0x79, F11 = 0x7A, F12 = 0x7B, VolDown = 0xAE, Letter = 0x41;
    private static readonly KeyBinding ShiftBinding = new(Modifiers.Shift, Target.Process("spotify.exe"), 5);

    private static KeyDecider Decider() => new(
        KeyRemapMap.From(new[] { (MediaKey.Mute, "F10"), (MediaKey.VolumeDown, "F11") }),
        mods => mods == Modifiers.Shift ? ShiftBinding : null);

    [Fact]
    public void Real_media_key_with_a_binding_runs_it()
    {
        var d = Decider().Decide(VolDown, Modifiers.Shift);
        Assert.Equal(new KeyDecision(MediaKey.VolumeDown, ShiftBinding), d);
    }

    [Fact]
    public void Real_media_key_without_a_binding_passes_through()
    {
        Assert.Null(Decider().Decide(VolDown, Modifiers.None));
        Assert.Null(Decider().Decide(VolDown, Modifiers.Ctrl));
    }

    [Fact]
    public void Remapped_key_with_a_binding_resolves_like_the_real_key()
    {
        Assert.Equal(new KeyDecision(MediaKey.VolumeDown, ShiftBinding), Decider().Decide(F11, Modifiers.Shift));
        Assert.Equal(new KeyDecision(MediaKey.Mute, ShiftBinding), Decider().Decide(F10, Modifiers.Shift));
    }

    [Fact]
    public void Remapped_key_without_a_binding_becomes_the_real_key()
    {
        Assert.Equal(new KeyDecision(MediaKey.VolumeDown, null), Decider().Decide(F11, Modifiers.None));
        Assert.Equal(new KeyDecision(MediaKey.Mute, null), Decider().Decide(F10, Modifiers.Ctrl)); // unbound combination
    }

    [Fact]
    public void Other_keys_are_never_handled()
    {
        var d = Decider();
        Assert.False(d.IsHandled(Letter));
        Assert.False(d.IsHandled(F12));    // not remapped
        Assert.Null(d.Decide(F12, Modifiers.Shift));
        Assert.True(d.IsHandled(F11));
        Assert.True(d.IsHandled(VolDown));
    }

    [Fact]
    public void Latch_keeps_the_decision_when_the_modifier_changes_mid_hold()
    {
        var d = Decider();
        var latch = new KeyLatch<KeyDecision>();
        var first = latch.OnDown(F11, () => d.Decide(F11, Modifiers.Shift), out var repeat0);
        var second = latch.OnDown(F11, () => d.Decide(F11, Modifiers.None), out var repeat1); // Shift released
        Assert.False(repeat0);
        Assert.True(repeat1);
        Assert.Same(first, second);
        Assert.True(latch.OnUp(F11, out var latched));
        Assert.Same(first, latched);
        Assert.NotNull(latched!.Binding);
    }

    [Fact]
    public void Latch_remembers_a_translated_press_for_its_key_up()
    {
        var d = Decider();
        var latch = new KeyLatch<KeyDecision>();
        latch.OnDown(F10, () => d.Decide(F10, Modifiers.None), out _);
        Assert.True(latch.OnUp(F10, out var latched));
        Assert.Equal(new KeyDecision(MediaKey.Mute, null), latched);
    }

    [Fact]
    public void RemapMap_ignores_typing_keys_unknown_names_and_duplicates()
    {
        var map = KeyRemapMap.From(new[] { (MediaKey.Mute, "A"), (MediaKey.Next, "Nope"), (MediaKey.VolumeUp, "F12"), (MediaKey.Previous, "F12") });
        Assert.False(map.TryGet(0x41, out _));
        Assert.True(map.TryGet(F12, out var key));
        Assert.Equal(MediaKey.VolumeUp, key);   // first entry wins
        Assert.Equal("F12", map.KeyNameOf(MediaKey.VolumeUp));
        Assert.Null(map.KeyNameOf(MediaKey.Mute));
    }

    [Fact]
    public void ToVirtualKey_inverts_FromVirtualKey()
    {
        foreach (var k in Enum.GetValues<MediaKey>())
            Assert.Equal(k, MediaKeyMap.FromVirtualKey(MediaKeyMap.ToVirtualKey(k)));
    }
}

public class KeyRemapSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-remap-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "settings.json");

    public KeyRemapSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_to_none_and_roundtrips_by_key_name()
    {
        Assert.Empty(AppSettings.CreateDefault().KeyRemaps);
        new SettingsStore(File_).Save(new AppSettings { KeyRemaps = { new KeyRemap(MediaKey.Mute, "F10"), new KeyRemap(MediaKey.VolumeUp, "f12") } });
        Assert.Contains("\"Action\": \"Mute\"", File.ReadAllText(File_));
        var loaded = new SettingsStore(File_).Load().KeyRemaps;
        Assert.Equal(new[] { new KeyRemap(MediaKey.Mute, "F10"), new KeyRemap(MediaKey.VolumeUp, "F12") }, loaded);
    }

    [Fact]
    public void Load_drops_typing_keys_unknown_keys_and_conflicts()
    {
        File.WriteAllText(File_, "{\"KeyRemaps\":[" +
            "{\"Action\":\"Mute\",\"Key\":\"A\"}," +            // typing key
            "{\"Action\":\"Next\",\"Key\":\"Banana\"}," +       // unknown
            "{\"Action\":\"VolumeUp\",\"Key\":\"F12\"}," +      // ok
            "{\"Action\":\"VolumeDown\",\"Key\":\"f12\"}," +    // key already used
            "{\"Action\":\"VolumeUp\",\"Key\":\"F11\"}," +      // action already used
            "{\"Action\":\"Previous\",\"Key\":\"Numpad1\"}]}");
        var loaded = new SettingsStore(File_).Load().KeyRemaps;
        Assert.Equal(new[] { new KeyRemap(MediaKey.VolumeUp, "F12"), new KeyRemap(MediaKey.Previous, "Numpad1") }, loaded);
    }

    [Fact]
    public void Validate_flags_conflicts_and_refused_keys()
    {
        var s = new AppSettings { KeyRemaps = { new KeyRemap(MediaKey.Mute, "F10"), new KeyRemap(MediaKey.Next, "F10"), new KeyRemap(MediaKey.Mute, "F9"), new KeyRemap(MediaKey.Previous, "Space") } };
        var errors = s.Validate().Where(e => e.StartsWith("Media keys")).ToList();
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("F10 is assigned to two actions"));
        Assert.Contains(errors, e => e.Contains("Mute has two keys"));
        Assert.Contains(errors, e => e.Contains("Space"));
    }

    [Fact]
    public void Tray_line_lists_the_remaps()
    {
        Assert.Empty(ShortcutSummary.RemapLines(Array.Empty<KeyRemap>()));
        var line = Assert.Single(ShortcutSummary.RemapLines(new[] { new KeyRemap(MediaKey.Mute, "F10"), new KeyRemap(MediaKey.VolumeDown, "F11") }));
        Assert.Equal("Keys: F10 Mute, F11 Vol−", line);
    }
}
