namespace SonicScrewdriver.Input;

public enum MediaKey
{
    VolumeUp,
    VolumeDown,
    Mute,
    PlayPause,
    Next,
    Previous,
}

public static class MediaKeyMap
{
    public const int VK_VOLUME_MUTE = 0xAD;
    public const int VK_VOLUME_DOWN = 0xAE;
    public const int VK_VOLUME_UP = 0xAF;
    public const int VK_MEDIA_NEXT_TRACK = 0xB0;
    public const int VK_MEDIA_PREV_TRACK = 0xB1;
    public const int VK_MEDIA_PLAY_PAUSE = 0xB3;

    public static MediaKey? FromVirtualKey(int vk) => vk switch
    {
        VK_VOLUME_UP => MediaKey.VolumeUp,
        VK_VOLUME_DOWN => MediaKey.VolumeDown,
        VK_VOLUME_MUTE => MediaKey.Mute,
        VK_MEDIA_PLAY_PAUSE => MediaKey.PlayPause,
        VK_MEDIA_NEXT_TRACK => MediaKey.Next,
        VK_MEDIA_PREV_TRACK => MediaKey.Previous,
        _ => null,
    };
}
