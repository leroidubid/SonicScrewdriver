using SonicScrewdriver.Media;

namespace SonicScrewdriver.Tests.Media;

public class MediaSessionMatcherTests
{
    [Theory]
    [InlineData("Spotify.exe", "spotify.exe", true)]
    [InlineData("Chrome", "chrome.exe", true)]
    [InlineData("MSEdge", "msedge.exe", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "spotify.exe", true)]
    [InlineData("Chrome", "spotify.exe", false)]
    [InlineData("", "spotify.exe", false)]
    [InlineData("308046B0AF4A39CB", "firefox.exe", true)]   // Firefox registers a hash of its install path
    [InlineData("308046B0AF4A39CB", "spotify.exe", false)]
    [InlineData("NOTAHEXSTRING123", "firefox.exe", false)]
    public void Matches(string aumid, string exe, bool expected)
        => Assert.Equal(expected, MediaSessionMatcher.Matches(aumid, exe));
}
