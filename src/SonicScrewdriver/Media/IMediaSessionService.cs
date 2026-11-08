using SonicScrewdriver.Input;

namespace SonicScrewdriver.Media;

public enum MediaResult
{
    Ok,
    NoSession,
    Failed,
}

public interface IMediaSessionService
{
    Task<MediaResult> SendAsync(string exe, MediaKey key);
}
