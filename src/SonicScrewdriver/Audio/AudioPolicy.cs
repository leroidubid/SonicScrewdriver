using System.Runtime.InteropServices;

namespace SonicScrewdriver.Audio;

/// <summary>
/// Undocumented Windows audio policy interfaces (the same ones EarTrumpet and the Windows
/// "App volume and device preferences" page use): set the default output device, and set
/// the output device of one process. Called through raw vtable slots so no WinRT projection
/// or per-build interface types are needed. Every call returns false instead of throwing.
/// </summary>
public static class AudioPolicy
{
    private const int ERender = 0;
    private const int EConsole = 0, EMultimedia = 1, ECommunications = 2;
    private const string RenderInterface = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    // IPolicyConfig (Windows 7+): IUnknown (3) + 10 methods, then SetDefaultEndpoint.
    private static readonly Guid ClsidPolicyConfig = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    private static readonly Guid IidPolicyConfig = new("f8679f50-850a-41cf-9c72-430f290290c8");
    private const int SlotSetDefaultEndpoint = 3 + 10;

    // IAudioPolicyConfigFactory: IUnknown (3) + IInspectable (3) + 19 methods, then Set/GetPersistedDefaultAudioEndpoint.
    private static readonly Guid IidFactoryPre21H2 = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
    private static readonly Guid IidFactory21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    private const int SlotSetPersisted = 3 + 3 + 19;
    private const int SlotGetPersisted = SlotSetPersisted + 1;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDefaultEndpointFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPersistedFn(IntPtr self, uint processId, int flow, int role, IntPtr deviceIdHString);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPersistedFn(IntPtr self, uint processId, int flow, int role, out IntPtr deviceIdHString);

    /// <summary>Makes <paramref name="deviceId"/> (an MMDevice ID) the default output for every role.</summary>
    public static bool SetDefaultDevice(string deviceId)
    {
        IntPtr unknown = IntPtr.Zero, config = IntPtr.Zero;
        object? obj = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidPolicyConfig, throwOnError: true)!;
            obj = Activator.CreateInstance(type)!;
            unknown = Marshal.GetIUnknownForObject(obj);
            var iid = IidPolicyConfig;
            if (Marshal.QueryInterface(unknown, ref iid, out config) < 0) return false;
            var set = Method<SetDefaultEndpointFn>(config, SlotSetDefaultEndpoint);
            var ok = true;
            foreach (var role in new[] { EConsole, EMultimedia, ECommunications })
                ok &= set(config, deviceId, role) >= 0;
            return ok;
        }
        catch (Exception) { return false; }
        finally
        {
            if (config != IntPtr.Zero) Marshal.Release(config);
            if (unknown != IntPtr.Zero) Marshal.Release(unknown);
            if (obj is not null) Marshal.ReleaseComObject(obj);
        }
    }

    /// <summary>Routes a process to an output device; null restores "follow the default device".</summary>
    public static bool SetAppDevice(int processId, string? deviceId)
    {
        var factory = Factory();
        if (factory == IntPtr.Zero) return false;
        var hstring = IntPtr.Zero;
        try
        {
            if (deviceId is not null)
            {
                var full = PersistedId(deviceId);
                if (WindowsCreateString(full, full.Length, out hstring) < 0) return false;
            }
            var set = Method<SetPersistedFn>(factory, SlotSetPersisted);
            return set(factory, (uint)processId, ERender, EMultimedia, hstring) >= 0
                && set(factory, (uint)processId, ERender, EConsole, hstring) >= 0;
        }
        catch (Exception) { return false; }
        finally
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            Marshal.Release(factory);
        }
    }

    /// <summary>The MMDevice ID a process is routed to, or null when it follows the default device.</summary>
    public static string? GetAppDevice(int processId)
    {
        var factory = Factory();
        if (factory == IntPtr.Zero) return null;
        try
        {
            var get = Method<GetPersistedFn>(factory, SlotGetPersisted);
            if (get(factory, (uint)processId, ERender, EMultimedia, out var h) < 0 || h == IntPtr.Zero) return null;
            try
            {
                var raw = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(h, out _));
                return raw is null ? null : MmDeviceIdFromPersisted(raw);
            }
            finally { WindowsDeleteString(h); }
        }
        catch (Exception) { return null; }
        finally { Marshal.Release(factory); }
    }

    internal static string PersistedId(string deviceId) => $@"\\?\SWD#MMDEVAPI#{deviceId}#{RenderInterface}";

    internal static string? MmDeviceIdFromPersisted(string persisted)
    {
        const string prefix = @"\\?\SWD#MMDEVAPI#";
        if (!persisted.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = persisted[prefix.Length..];
        var hash = rest.LastIndexOf('#');
        return hash > 0 ? rest[..hash] : rest;
    }

    private static IntPtr Factory()
    {
        var hstring = IntPtr.Zero;
        try
        {
            const string cls = "Windows.Media.Internal.AudioPolicyConfig";
            if (WindowsCreateString(cls, cls.Length, out hstring) < 0) return IntPtr.Zero;
            var iid = Environment.OSVersion.Version.Build >= 21390 ? IidFactory21H2 : IidFactoryPre21H2;
            return RoGetActivationFactory(hstring, ref iid, out var factory) >= 0 ? factory : IntPtr.Zero;
        }
        catch (Exception) { return IntPtr.Zero; }
        finally { if (hstring != IntPtr.Zero) WindowsDeleteString(hstring); }
    }

    private static T Method<T>(IntPtr comObject, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(comObject);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
}
