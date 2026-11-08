using System.Windows;
using SonicScrewdriver.Actions;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Interop;
using SonicScrewdriver.Media;
using SonicScrewdriver.Settings;
using SonicScrewdriver.UI;

namespace SonicScrewdriver;

public partial class App : Application
{
    private const string MutexName = @"Local\SonicScrewdriver.SingleInstance";
    private const string ShowEventName = @"Local\SonicScrewdriver.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    private SettingsStore _store = null!;
    private AppSettings _settings = null!;
    private volatile BindingResolver _resolver = new(Array.Empty<KeyBinding>());
    private AudioSessionService _audio = null!;
    private ActionDispatcher _dispatcher = null!;
    private OsdWindow _osd = null!;
    private KeyboardHook? _hook;
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Already running: ask the first instance to show its flyout, then quit.
            // This process was just launched by the user, so it may hand foreground rights to the first instance.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (Exception) { }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowFlyout), null, Timeout.Infinite, false);

        try
        {
            StartServices();
        }
        catch (Exception ex)
        {
            // Never linger as an invisible process holding the single-instance mutex.
            MessageBox.Show($"Sonic Screwdriver could not start: {ex.Message}", "Sonic Screwdriver", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Only once startup succeeded: keep running through unexpected UI exceptions.
        DispatcherUnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine(args.Exception);
            args.Handled = true;
        };

        // Once startup settles, give unused memory back to Windows.
        Dispatcher.BeginInvoke(MemoryTrim.Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void StartServices()
    {
        _store = new SettingsStore(SettingsStore.DefaultPath);
        _settings = _store.Load();
        _resolver = new BindingResolver(_settings.Bindings);

        _audio = new AudioSessionService();
        _osd = new OsdWindow();
        _dispatcher = new ActionDispatcher(_audio, new MediaSessionService(), new ForegroundProcess(), _osd);
        _dispatcher.CycleDeviceIds = _settings.CycleDeviceIds;
        _tray = new TrayIcon();
        _tray.DefaultDeviceName = () => _audio.GetDevices().FirstOrDefault(d => d.IsDefault)?.Name;
        _tray.SetShortcuts(ShortcutSummary.Lines(_settings.Bindings));
        _tray.OpenFlyoutRequested += (_, _) => ShowFlyout();
        _tray.OpenSettingsRequested += (_, _) => OpenSettings();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.WheelScrolled += (_, notches) =>
        {
            var v = Math.Clamp(_audio.MasterVolume + notches * (float)(WheelVolume.StepPerNotch / 100), 0f, 1f);
            _audio.MasterVolume = v;
            _osd.Show(null, "Master volume", $"{(int)MathF.Round(v * 100)} %");
        };

        _hook = new KeyboardHook(mods => _resolver.Resolve(mods), OnMediaAction);
        try
        {
            _hook.Install();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not install the keyboard hook: {ex.Message}", "Sonic Screwdriver",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (_store.RecoveredFromCorruption)
            _tray.Notify("Sonic Screwdriver", "Settings file was unreadable: defaults restored (backup in settings.json.bak).");
    }

    /// <summary>Persists settings (plus an optional registry change); reports failures instead of hiding them.</summary>
    private void TrySave(Action? alsoDo = null)
    {
        try
        {
            alsoDo?.Invoke();
            _store.Save(_settings);
        }
        catch (Exception ex)
        {
            _tray?.Notify("Sonic Screwdriver", $"Could not save settings: {ex.Message}. Changes apply until Sonic Screwdriver restarts.");
        }
    }

    /// <summary>The flyout is created on first use and trims memory when hidden.</summary>
    private void ShowFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow(_audio);
            _flyout.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is false)
                    Dispatcher.BeginInvoke(MemoryTrim.Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }
        _flyout.ShowNearTray();
    }

    /// <summary>Runs on the hook thread: only posts the work to the UI thread.</summary>
    private void OnMediaAction(KeyBinding binding, MediaKey key) =>
        Dispatcher.BeginInvoke(async () =>
        {
            try { await _dispatcher.ExecuteAsync(binding, key); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        });

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _audio);
        _settingsWindow.Saved += s =>
        {
            _settings = s;
            _resolver = new BindingResolver(s.Bindings);
            _dispatcher.CycleDeviceIds = s.CycleDeviceIds;
            _tray?.SetShortcuts(ShortcutSummary.Lines(s.Bindings));
            TrySave(() => StartupRegistration.Set(s.StartWithWindows));
        };
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            Dispatcher.BeginInvoke(MemoryTrim.Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _audio?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
