using SonicScrewdriver.Input;

namespace SonicScrewdriver.Tests.Input;

public class MediaKeyMapTests
{
    [Theory]
    [InlineData(0xAF, MediaKey.VolumeUp)]
    [InlineData(0xAE, MediaKey.VolumeDown)]
    [InlineData(0xAD, MediaKey.Mute)]
    [InlineData(0xB3, MediaKey.PlayPause)]
    [InlineData(0xB0, MediaKey.Next)]
    [InlineData(0xB1, MediaKey.Previous)]
    public void FromVirtualKey_maps_media_keys(int vk, MediaKey expected)
        => Assert.Equal(expected, MediaKeyMap.FromVirtualKey(vk));

    [Fact]
    public void FromVirtualKey_returns_null_for_other_keys()
        => Assert.Null(MediaKeyMap.FromVirtualKey(0x41));
}
