using System.IO;
using SonicScrewdriver.Automation;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Automation;

public class AutoCompetitiveTests
{
    [Fact]
    public void Turns_on_when_a_program_starts_and_off_when_it_exits()
    {
        var a = new AutoCompetitive();
        Assert.Null(a.Evaluate(anyRunning: false, modeOn: false));
        Assert.True(a.Evaluate(true, false));
        Assert.True(a.IsAutomatic);
        Assert.Null(a.Evaluate(true, true));          // still running: nothing to do
        Assert.False(a.Evaluate(false, true));        // exited
        Assert.False(a.IsAutomatic);
        Assert.Null(a.Evaluate(false, false));
    }

    [Fact]
    public void Does_not_touch_a_mode_the_user_switched_on()
    {
        var a = new AutoCompetitive();
        Assert.Null(a.Evaluate(true, true));          // already on (by hand): not ours
        Assert.Null(a.Evaluate(false, true));         // so it stays on when the program exits
    }

    [Fact]
    public void Manual_switch_while_automatic_keeps_the_mode_after_exit()
    {
        var a = new AutoCompetitive();
        a.Evaluate(true, false);
        a.UserChanged(modeOn: true);                  // the user confirms it by hand
        Assert.False(a.IsAutomatic);
        Assert.Null(a.Evaluate(false, true));
    }

    [Fact]
    public void Manual_off_holds_until_the_program_has_exited()
    {
        var a = new AutoCompetitive();
        a.Evaluate(true, false);                      // automatic on
        a.UserChanged(modeOn: false);                 // user turns it off during the game
        Assert.Null(a.Evaluate(true, false));         // must not come back while the game runs
        Assert.Null(a.Evaluate(false, false));        // game exits: released
        Assert.True(a.Evaluate(true, false));         // next launch turns it on again
    }

    [Fact]
    public void FirstRunning_ignores_disabled_entries_and_matches_case_insensitively()
    {
        var programs = new[] { new AutoProgram("a.exe", false), new AutoProgram("game.exe") };
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A.exe", "GAME.EXE" };
        Assert.Equal("game.exe", AutoCompetitive.FirstRunning(programs, running));
        Assert.Null(AutoCompetitive.FirstRunning(programs, new HashSet<string> { "a.exe" }));
    }

    [Fact]
    public void ProcessNames_lists_the_test_host_without_opening_anything()
    {
        var names = ProcessNames.Running();
        Assert.Contains(Path.GetFileName(Environment.ProcessPath)!.ToLowerInvariant(), names);
    }
}

public class AutoProgramSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-auto-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "settings.json");

    public AutoProgramSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_to_an_empty_list_and_roundtrips()
    {
        Assert.Empty(AppSettings.CreateDefault().AutoPrograms);
        new SettingsStore(File_).Save(new AppSettings { AutoPrograms = { new AutoProgram("valorant.exe"), new AutoProgram("csgo.exe", false) } });
        var loaded = new SettingsStore(File_).Load().AutoPrograms;
        Assert.Equal(new[] { new AutoProgram("valorant.exe", true), new AutoProgram("csgo.exe", false) }, loaded);
    }

    [Fact]
    public void Load_normalizes_names_drops_empty_and_duplicates()
    {
        File.WriteAllText(File_, "{\"AutoPrograms\":[{\"ExeName\":\" Game \",\"Enabled\":true},{\"ExeName\":\"\"},{\"ExeName\":\"GAME.EXE\",\"Enabled\":false},{\"ExeName\":\"x.exe\",\"Enabled\":false}]}");
        var loaded = new SettingsStore(File_).Load().AutoPrograms;
        Assert.Equal(new[] { new AutoProgram("game.exe", true), new AutoProgram("x.exe", false) }, loaded);
    }

    [Fact]
    public void Validate_flags_empty_and_duplicate_names()
    {
        var s = new AppSettings { AutoPrograms = { new AutoProgram(""), new AutoProgram("a"), new AutoProgram("A.EXE") } };
        var errors = s.Validate().Where(e => e.StartsWith("Automatic mode")).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains("row 1", errors[0]);
        Assert.Contains("row 3", errors[1]);
    }
}
