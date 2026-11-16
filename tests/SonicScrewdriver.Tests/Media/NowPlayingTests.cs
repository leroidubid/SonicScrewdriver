using SonicScrewdriver.Audio;
using SonicScrewdriver.Media;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;
using SonicScrewdriver.UI;

namespace SonicScrewdriver.Tests.Media;

public class NowPlayingTests
{
    [Fact]
    public void Text_is_artist_dash_title_or_the_title_alone()
    {
        Assert.Equal("Daft Punk — Around the World", new NowPlaying("Around the World", "Daft Punk", true).Text);
        Assert.Equal("A video", new NowPlaying("A video", "", false).Text);
        Assert.Equal("A video", new NowPlaying("A video", "  ", false).Text);
    }

    [Fact]
    public void Row_shows_the_line_and_the_play_or_pause_glyph_and_hides_it_when_cleared()
    {
        var audio = new FakeAudio();
        var vm = new AppVolumeViewModel(audio, "dev", new AudioApp("spotify.exe", "Spotify", 1, 0.5f, false));
        Assert.False(vm.HasNowPlaying);
        Assert.Equal("", vm.PlayGlyph); // play
        vm.SetNowPlaying(new NowPlaying("Song", "Artist", true));
        Assert.True(vm.HasNowPlaying);
        Assert.Equal("Artist — Song", vm.NowPlayingText);
        Assert.Equal("", vm.PlayGlyph); // pause
        vm.SetNowPlaying(null);
        Assert.False(vm.HasNowPlaying);
        Assert.Equal("", vm.PlayGlyph);
    }

    [Fact]
    public void Feature_is_off_by_default_and_roundtrips()
    {
        Assert.False(new AppSettings().NowPlayingInMixer);
        Assert.False(AppSettings.CreateDefault().NowPlayingInMixer);
    }

    [Fact]
    public async Task Fake_service_picks_the_playing_session_through_the_matcher()
    {
        var media = new FakeMedia { Playing = new NowPlaying("T", "A", true) };
        Assert.Equal("A — T", (await media.GetNowPlayingAsync("x.exe"))!.Text);
        media.Watch(() => { });
        media.Watch(null);
        Assert.Equal(2, media.Watchers.Count);
    }
}
