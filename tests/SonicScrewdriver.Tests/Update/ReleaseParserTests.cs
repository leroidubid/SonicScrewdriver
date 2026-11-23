using SonicScrewdriver.Update;
using Xunit;

namespace SonicScrewdriver.Tests.Update;

public class ReleaseParserTests
{
    private const string Base = "https://github.com/leroidubid/SonicScrewdriver/releases/download/v1.3.0/";

    private static string Json(string tag = "v1.3.0", bool draft = false, bool pre = false, string? installerUrl = null, bool sums = true, string installerName = "SonicScrewdriver-Setup-1.3.0.exe") =>
        $$"""
        {"tag_name":"{{tag}}","draft":{{draft.ToString().ToLower()}},"prerelease":{{pre.ToString().ToLower()}},"assets":[
          {"name":"{{installerName}}","browser_download_url":"{{installerUrl ?? Base + installerName}}"}
          {{(sums ? $$""",{"name":"SHA256SUMS.txt","browser_download_url":"{{Base}}SHA256SUMS.txt"}""" : "")}}
        ]}
        """;

    [Fact]
    public void A_stable_release_with_installer_and_checksums_is_read()
    {
        var r = ReleaseParser.ParseLatest(Json());
        Assert.NotNull(r);
        Assert.Equal(new Version(1, 3, 0), r!.Version);
        Assert.Equal("SonicScrewdriver-Setup-1.3.0.exe", r.InstallerName);
        Assert.Equal(Base + "SHA256SUMS.txt", r.ChecksumsUrl.ToString());
        Assert.Null(r.Notes);
    }

    [Fact]
    public void The_release_body_becomes_plain_notes()
    {
        var json = Json().Replace("\"draft\"", "\"body\":\"### Added\\r\\n- **Updates** card with `notes`\\r\\n\",\"draft\"");
        Assert.Equal("Added\n- Updates card with notes", ReleaseParser.ParseLatest(json)!.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n ## \r\n")]
    public void Empty_notes_are_null(string? body) => Assert.Null(ReleaseParser.CleanNotes(body));

    [Fact]
    public void Long_notes_are_cut()
    {
        var notes = ReleaseParser.CleanNotes(new string('a', 5000))!;
        Assert.Equal(2001, notes.Length);
        Assert.EndsWith("…", notes);
    }

    [Fact]
    public void The_skipped_version_is_kept_in_the_settings_file()
    {
        var settings = new SonicScrewdriver.Settings.AppSettings { SkippedUpdateVersion = "1.4.0" };
        var back = System.Text.Json.JsonSerializer.Deserialize<SonicScrewdriver.Settings.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal("1.4.0", back.SkippedUpdateVersion);
        Assert.Equal("", new SonicScrewdriver.Settings.AppSettings().SkippedUpdateVersion);
    }

    [Theory]
    [InlineData("v1.3.0-rc1")]
    [InlineData("1.3.0")]
    [InlineData("v1.3")]
    [InlineData("latest")]
    public void Other_tags_are_refused(string tag) => Assert.Null(ReleaseParser.ParseLatest(Json(tag)));

    [Fact]
    public void Drafts_and_prereleases_are_refused()
    {
        Assert.Null(ReleaseParser.ParseLatest(Json(draft: true)));
        Assert.Null(ReleaseParser.ParseLatest(Json(pre: true)));
    }

    [Fact]
    public void Missing_checksums_or_installer_refuse_the_release()
    {
        Assert.Null(ReleaseParser.ParseLatest(Json(sums: false)));
        Assert.Null(ReleaseParser.ParseLatest(Json(installerName: "Other.exe")));
    }

    [Theory]
    [InlineData("https://evil.example/SonicScrewdriver-Setup-1.3.0.exe")]
    [InlineData("https://github.com/someone/else/releases/download/v1.3.0/SonicScrewdriver-Setup-1.3.0.exe")]
    [InlineData("http://github.com/leroidubid/SonicScrewdriver/releases/download/v1.3.0/SonicScrewdriver-Setup-1.3.0.exe")]
    [InlineData("https://github.com/leroidubid/SonicScrewdriver/releases/download/v1.2.0/SonicScrewdriver-Setup-1.3.0.exe")]
    public void Download_links_outside_the_release_are_refused(string url) =>
        Assert.Null(ReleaseParser.ParseLatest(Json(installerUrl: url)));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":1,\"assets\":[]}")]
    public void Garbage_is_refused(string json) => Assert.Null(ReleaseParser.ParseLatest(json));

    [Theory]
    [InlineData("1.2.1", "1.2.0", true)]
    [InlineData("1.3.0", "1.2.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2.0", "1.2.1", false)]
    public void Newer_compares_major_minor_patch(string latest, string current, bool expected) =>
        Assert.Equal(expected, ReleaseParser.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void The_revision_number_is_ignored() =>
        Assert.False(ReleaseParser.IsNewer(new Version(1, 2, 0), new Version(1, 2, 0, 5)));

    [Fact]
    public void A_hash_is_found_by_file_name()
    {
        var h1 = new string('a', 64);
        var h2 = new string('B', 64);
        var sums = $"{h1}  SonicScrewdriver.exe\n{h2} *SonicScrewdriver-Setup-1.3.0.exe\r\n";
        Assert.Equal(new string('b', 64), ReleaseParser.HashFor(sums, "SonicScrewdriver-Setup-1.3.0.exe"));
        Assert.Equal(h1, ReleaseParser.HashFor(sums, "SonicScrewdriver.exe"));
        Assert.Null(ReleaseParser.HashFor(sums, "Other.exe"));
    }

    [Fact]
    public void A_malformed_hash_line_is_ignored()
    {
        Assert.Null(ReleaseParser.HashFor("zz  SonicScrewdriver-Setup-1.3.0.exe\n", "SonicScrewdriver-Setup-1.3.0.exe"));
        Assert.Null(ReleaseParser.HashFor(new string('g', 64) + "  f.exe\n", "f.exe"));
    }

    [Fact]
    public void Updates_are_off_by_default() => Assert.False(new SonicScrewdriver.Settings.AppSettings().CheckUpdatesAtStart);
}

public class UpdateRelaunchTests
{
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\SonicScrewdriver\SonicScrewdriver.exe", @"C:\Users\me\AppData\Local\Programs\SonicScrewdriver\", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\SonicScrewdriver\SonicScrewdriver.exe", @"c:\users\ME\appdata\local\programs\sonicscrewdriver", true)]
    [InlineData(@"D:\Tools\SonicScrewdriver.exe", @"C:\Users\me\AppData\Local\Programs\SonicScrewdriver", false)]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\SonicScrewdriver\sub\SonicScrewdriver.exe", @"C:\Users\me\AppData\Local\Programs\SonicScrewdriver", false)]
    [InlineData(@"D:\Tools\SonicScrewdriver.exe", null, false)]
    [InlineData(null, @"C:\x", false)]
    public void An_installed_copy_is_the_one_in_the_install_folder(string? exe, string? location, bool expected) =>
        Assert.Equal(expected, UpdateRelaunch.IsInstalledCopy(exe, location));

    [Fact]
    public void The_relaunch_argument_round_trips()
    {
        Assert.True(UpdateRelaunch.TryParse(UpdateRelaunch.ArgumentFor(4321).Split(' '), out var pid));
        Assert.Equal(4321, pid);
    }

    [Theory]
    [InlineData("--updated-from")]
    [InlineData("--updated-from 0")]
    [InlineData("--updated-from -5")]
    [InlineData("--updated-from abc")]
    [InlineData("--updated-from 5 --mute-all")]
    [InlineData("--mute-all 5")]
    public void Other_arguments_are_not_a_relaunch(string args) =>
        Assert.False(UpdateRelaunch.TryParse(args.Split(' '), out _));

    [Fact]
    public void The_exe_asset_of_a_release_is_read_when_present()
    {
        var b = "https://github.com/leroidubid/SonicScrewdriver/releases/download/v1.3.1/";
        var json = $$"""{"tag_name":"v1.3.1","assets":[{"name":"SonicScrewdriver-Setup-1.3.1.exe","browser_download_url":"{{b}}SonicScrewdriver-Setup-1.3.1.exe"},{"name":"SHA256SUMS.txt","browser_download_url":"{{b}}SHA256SUMS.txt"},{"name":"SonicScrewdriver.exe","browser_download_url":"{{b}}SonicScrewdriver.exe"}]}""";
        Assert.Equal(b + "SonicScrewdriver.exe", ReleaseParser.ParseLatest(json)!.ExeUrl!.ToString());
    }
}
