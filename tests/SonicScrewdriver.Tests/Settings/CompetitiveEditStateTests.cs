using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Settings;

/// <summary>The window's competitive-mode box must never flip the live mode by itself (second review, finding 1).</summary>
public class CompetitiveEditStateTests
{
    // What the app does on Save: apply ValueToSave(live), then tell the window.
    private static bool Save(CompetitiveEditState state, ref bool live)
    {
        live = state.ValueToSave(live);
        state.Saved(live);
        return live;
    }

    [Fact]
    public void Open_toggle_by_shortcut_save_save_keeps_the_live_mode()
    {
        var live = false;
        var state = new CompetitiveEditState(live);
        live = true; state.LiveChanged(live);       // a shortcut turns the mode on while the window is open
        Assert.True(state.Box);                      // the box follows
        Assert.True(Save(state, ref live));
        Assert.True(Save(state, ref live));          // the second Save must not turn it off
    }

    [Fact]
    public void Save_after_a_toggle_without_syncing_the_box_still_keeps_the_live_mode_twice()
    {
        var live = false;
        var state = new CompetitiveEditState(live);
        live = true;                                 // toggled, the window was not told
        Assert.True(Save(state, ref live));
        Assert.True(state.Box);                      // box resynchronised by the save
        Assert.True(Save(state, ref live));
    }

    [Fact]
    public void Open_tick_save_shortcut_toggles_save_keeps_the_new_live_mode()
    {
        var live = false;
        var state = new CompetitiveEditState(live);
        state.Box = true;                            // the user ticks the box
        Assert.True(state.Edited);
        Assert.True(Save(state, ref live));          // saved: on
        live = false; state.LiveChanged(live);       // a shortcut turns it off
        Assert.False(Save(state, ref live));         // stays off
    }

    [Fact]
    public void An_edited_box_wins_over_a_live_change()
    {
        var live = false;
        var state = new CompetitiveEditState(live);
        state.Box = true;
        live = true; state.LiveChanged(live);
        live = false; state.LiveChanged(live);
        Assert.True(state.Box);                      // the user's pending choice is kept
        Assert.True(state.ValueToSave(live));
    }

    [Fact]
    public void An_unedited_box_is_not_edited_after_a_live_change()
    {
        var state = new CompetitiveEditState(false);
        state.LiveChanged(true);
        Assert.False(state.Edited);
    }
}
