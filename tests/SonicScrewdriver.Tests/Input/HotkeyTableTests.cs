using SonicScrewdriver.Input;

namespace SonicScrewdriver.Tests.Input;

/// <summary>The id bookkeeping of HotkeyRegistry, tested without a window or a real RegisterHotKey.</summary>
public class HotkeyTableTests
{
    private static Hotkey Chord(string text) { Assert.True(Hotkey.TryParse(text, out var h)); return h; }

    [Fact]
    public void Native_ids_follow_the_list_order_and_map_back_to_the_callers_id()
    {
        var table = new HotkeyTable();
        var failed = table.Fill(new[] { (Chord("Ctrl+Alt+F1"), "muteall"), (Chord("Ctrl+Alt+F2"), "profile:Game") }, (_, _) => true);
        Assert.Empty(failed);
        Assert.True(table.TryGetId(1, out var first));
        Assert.Equal("muteall", first);
        Assert.True(table.TryGetId(2, out var second));
        Assert.Equal("profile:Game", second);
        Assert.False(table.TryGetId(3, out _));
        Assert.Equal(new[] { 1, 2 }, table.NativeIds.OrderBy(i => i));
    }

    [Fact]
    public void A_refused_chord_is_returned_and_neither_mapped_nor_owned()
    {
        var table = new HotkeyTable();
        var refused = Chord("Ctrl+Alt+F2");
        var failed = table.Fill(new[] { (Chord("Ctrl+Alt+F1"), "a"), (refused, "b"), (Chord("Ctrl+Alt+F3"), "c") }, (_, h) => h != refused);
        Assert.Equal(new[] { refused }, failed);
        Assert.False(table.Owns(refused));
        Assert.True(table.Owns(Chord("Ctrl+Alt+F3")));
        Assert.False(table.TryGetId(2, out _));
        Assert.True(table.TryGetId(3, out var id)); // later chords keep their position
        Assert.Equal("c", id);
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void Fill_replaces_the_previous_content_and_clear_empties_it()
    {
        var table = new HotkeyTable();
        table.Fill(new[] { (Chord("Ctrl+Alt+F1"), "a") }, (_, _) => true);
        table.Fill(new[] { (Chord("Ctrl+Alt+F2"), "b") }, (_, _) => true);
        Assert.False(table.Owns(Chord("Ctrl+Alt+F1")));
        Assert.True(table.TryGetId(1, out var id));
        Assert.Equal("b", id);
        table.Clear();
        Assert.Equal(0, table.Count);
        Assert.False(table.Owns(Chord("Ctrl+Alt+F2")));
    }

    [Fact]
    public void When_every_chord_is_refused_nothing_is_registered()
    {
        var table = new HotkeyTable();
        var failed = table.Fill(new[] { (Chord("Ctrl+Alt+F1"), "a") }, (_, _) => false);
        Assert.Single(failed);
        Assert.Equal(0, table.Count);
    }
}
