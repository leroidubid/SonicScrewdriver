using System.Windows;
using SonicScrewdriver.Actions;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Automation;
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
    private volatile KeyDecider _decider = new(KeyRemapMap.Empty, _ => null);
    private AudioSessionService _audio = null!;
    private ActionDispatcher _dispatcher = null!;
    private OsdWindow _osd = null!;
    private KeyboardHook? _hook;
    private GlobalHotkey? _modeHotkey;
    private GlobalHotkeySet _freeHotkeys = null!;
    private readonly AutoCompetitive _autoMode = new();
    private readonly System.Windows.Threading.DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromSeconds(5) };
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
        _settings.StartWithWindows = StartupRegistration.IsEnabled(); // the installer may have set it
        ProcessInfo.Minimal = _settings.CompetitiveMode; // before any session is listed
        RebuildDecider();

        _audio = new AudioSessionService();
        _osd = new OsdWindow();
        _dispatcher = new ActionDispatcher(_audio, new MediaSessionService(), new ForegroundProcess(), _osd);
        _dispatcher.CycleDeviceIds = _settings.CycleDeviceIds;
        _tray = new TrayIcon();
        _tray.DefaultDeviceName = () => _audio.GetDevices().FirstOrDefault(d => d.IsDefault)?.Name;
        _tray.SetShortcuts(ShortcutLines(_settings));
        _tray.OpenFlyoutRequested += (_, _) => ShowFlyout();
        _tray.OpenSettingsRequested += (_, _) => OpenSettings();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.CompetitiveModeToggled += (_, on) => SetCompetitive(on, notify: false);
        _tray.WheelScrolled += (_, notches) =>
        {
            var v = Math.Clamp(_audio.MasterVolume + notches * (float)(WheelVolume.StepPerNotch / 100), 0f, 1f);
            _audio.MasterVolume = v;
            _osd.Show(null, "Master volume", $"{(int)MathF.Round(v * 100)} %");
        };

        _freeHotkeys = new GlobalHotkeySet();
        _freeHotkeys.Pressed += entry => OnMediaAction(entry.Binding, entry.Action);
        _hook = new KeyboardHook(() => _decider, OnMediaAction);
        ApplyMode(runtimeChange: false);

        // RegisterHotKey, not the keyboard hook: it keeps working while competitive mode has uninstalled the hook.
        _modeHotkey = new GlobalHotkey();
        _modeHotkey.Pressed += () => SetCompetitive(!_settings.CompetitiveMode, notify: true);
        RegisterModeHotkey();

        _autoTimer.Tick += (_, _) => PollAutoPrograms();
        PollAutoPrograms();

        if (_store.RecoveredFromCorruption)
            _tray.Notify("Sonic Screwdriver", "Settings file was unreadable: defaults restored (backup in settings.json.bak).");
    }

    /// <summary>
    /// Applies competitive mode. When on, nothing that an anti-cheat could flag runs: no keyboard hook (so no
    /// shortcuts), no OSD, no wheel registration on the tray icon, and no lookups of other processes.
    /// </summary>
    private void ApplyMode(bool runtimeChange = true)
    {
        var on = _settings.CompetitiveMode;
        ProcessInfo.Minimal = on;
        _osd.Enabled = !on;
        _tray!.WheelEnabled = !on;
        _tray.SetCompetitive(on);
        if (on) _hook!.Dispose();
        else InstallHook();
        ApplyFreeHotkeys();
        // Start the session list over: drops process handles when entering the mode, rediscovers names and icons when leaving it.
        if (runtimeChange) _audio.Reset();
    }

    /// <summary>The bindings and key remaps become what the keyboard hook decides with (read on the hook thread).</summary>
    private void RebuildDecider()
    {
        _resolver = new BindingResolver(_settings.Bindings);
        var resolver = _resolver;
        _decider = new KeyDecider(KeyRemapMap.From(_settings.KeyRemaps.Select(r => (r.Action, r.Key))), mods => resolver.Resolve(mods));
    }

    private static IReadOnlyList<string> ShortcutLines(AppSettings s) =>
        ShortcutSummary.Lines(s.Bindings).Concat(ShortcutSummary.RemapLines(s.KeyRemaps)).Concat(ShortcutSummary.FreeLines(s.FreeHotkeys)).ToList();

    /// <summary>
    /// Registers the free hotkeys with Windows (none while competitive mode is on: they are shortcuts).
    /// A chord that Windows or another program already owns is reported and skipped, never fatal.
    /// </summary>
    private void ApplyFreeHotkeys()
    {
        var failed = _freeHotkeys.Apply(_settings.CompetitiveMode ? FreeHotkeyMap.Empty : FreeHotkeyMap.From(_settings.FreeHotkeys));
        if (failed.Count > 0)
            _tray?.Notify("Sonic Screwdriver", $"Shortcut {string.Join(", ", failed)} is already used by Windows or another program and was not registered.");
    }

    /// <summary>Switches competitive mode from the tray menu or the shortcut, and saves the choice.</summary>
    private void SetCompetitive(bool on, bool notify)
    {
        _autoMode.UserChanged(on); // a manual switch always wins over the automatic list
        if (on == _settings.CompetitiveMode) return;
        _settings.CompetitiveMode = on;
        ApplyMode();
        TrySave();
        if (notify)
            _tray?.Notify("Competitive mode: " + (on ? "on" : "off"),
                on ? "Shortcuts, on-screen display and wheel are off." : "Shortcuts, on-screen display and wheel are back.");
    }

    /// <summary>
    /// Every 5 s while an enabled program is listed: one snapshot of the process names (no process is opened).
    /// Runs once more after the list changes, so that turning the last entry off also ends an automatic "on".
    /// </summary>
    private void PollAutoPrograms()
    {
        var any = _settings.AutoPrograms.Any(p => p.Enabled);
        var running = any ? ProcessNames.Running() : new HashSet<string>();
        var program = AutoCompetitive.FirstRunning(_settings.AutoPrograms, running);
        if (_autoMode.Evaluate(program is not null, _settings.CompetitiveMode) is { } on)
        {
            _settings.CompetitiveMode = on; // not saved: it follows the program, not a preference
            ApplyMode();
            _tray?.Notify("Competitive mode: " + (on ? "on" : "off"),
                on ? $"Turned on because {program} is running." : "The program has exited.");
        }
        _autoTimer.IsEnabled = any;
    }

    private void RegisterModeHotkey()
    {
        Hotkey? hotkey = Hotkey.TryParse(_settings.CompetitiveHotkey, out var h) ? h : null;
        _tray!.ModeHotkeyText = hotkey?.ToString();
        if (!_modeHotkey!.Register(hotkey))
            _tray?.Notify("Sonic Screwdriver", $"The shortcut {hotkey} for competitive mode is already used by another program.");
    }

    private void InstallHook()
    {
        try
        {
            _hook!.Install();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not install the keyboard hook: {ex.Message}", "Sonic Screwdriver",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Persists settings (plus an optional registry change); reports failures instead of hiding them.</summary>
    private void TrySave(Action? alsoDo = null)
    {
        try
        {
            alsoDo?.Invoke();
            // An automatic "on" follows a running program: it is never saved as the user's preference.
            var saved = _settings.CompetitiveMode;
            if (_autoMode.IsAutomatic) _settings.CompetitiveMode = false;
            try { _store.Save(_settings); }
            finally { _settings.CompetitiveMode = saved; }
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
        _settingsWindow = new SettingsWindow(_settings, _audio, h => _freeHotkeys.IsAvailable(h));
        _settingsWindow.Saved += s =>
        {
            if (!_settingsWindow.CompetitiveModeEdited) s.CompetitiveMode = _settings.CompetitiveMode; // keep a toggle made while the window was open
            var modeChanged = s.CompetitiveMode != _settings.CompetitiveMode;
            if (_settingsWindow!.CompetitiveModeEdited) _autoMode.UserChanged(s.CompetitiveMode);
            _settings = s;
            RebuildDecider();
            _dispatcher.CycleDeviceIds = s.CycleDeviceIds;
            _tray?.SetShortcuts(ShortcutLines(s));
            if (modeChanged) ApplyMode();
            else ApplyFreeHotkeys();
            RegisterModeHotkey();
            PollAutoPrograms();
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
        _autoTimer.Stop();
        _hook?.Dispose();
        _modeHotkey?.Dispose();
        _freeHotkeys?.Dispose();
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
