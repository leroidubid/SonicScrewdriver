using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SonicScrewdriver.Audio;

namespace SonicScrewdriver.UI;

/// <summary>Exe icons as frozen WPF images, cached by exe name. Null when unavailable (UI shows a fallback).</summary>
internal static class IconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    public static ImageSource? Get(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        if (Cache.TryGetValue(exe, out var hit)) return hit;
        var path = ProcessInfo.ImagePath(exe);
        if (path is null) return null; // not cached: the path may become known later
        return Cache.GetOrAdd(exe, _ => Load(path));
    }

    private static ImageSource? Load(string path)
    {
        var large = IntPtr.Zero;
        try
        {
            if (ExtractIconEx(path, 0, out large, IntPtr.Zero, 1) == 0 || large == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception) { return null; }
        finally { if (large != IntPtr.Zero) DestroyIcon(large); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
