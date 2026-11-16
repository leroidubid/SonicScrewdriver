using System.Windows;
using SonicScrewdriver.Actions;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Automation;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Cli;
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
    private MediaSessionService _media = null!;
    private OsdWindow _osd = null!;
    private KeyboardHook? _hook;
    private GlobalHotkey? _modeHotkey;
    private GlobalHotkeySet _freeHotkeys = null!;
    private readonly ActionHotkeySet _actionHotkeys = new();
    private MuteAll _muteAll = null!;
    private LimitEnforcer _enforcer = null!;
    private DuckingEngine _ducking = null!;
    private DuckingEngine _streamer = null!;
    private MicrophoneMeter? _micMeter;
    private readonly System.Windows.Threading.DispatcherTimer _streamTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Windows.Threading.DispatcherTimer _duckTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private MicrophoneService? _mic;
    private readonly DevicePriorityPolicy _devicePriority = new();
    private readonly System.Windows.Threading.DispatcherTimer _priorityTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private RoutingApplier _routing = null!;
    private readonly System.Windows.Threading.DispatcherTimer _routeTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly ProfileTriggers _profileTriggers = new();
    private readonly System.Windows.Threading.DispatcherTimer _profileRetry = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly ProfileRetry _retry = new();
    private readonly System.Windows.Threading.DispatcherTimer _limitTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly AutoCompetitive _autoMode = new();
    private readonly System.Windows.Threading.DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private SettingsWindow? _settingsWindow;
    private CommandRunner _runner = null!;
    private System.Windows.Interop.HwndSource? _commandWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A command line call (--set, --mute, ...) is parsed first: it never starts a second instance.
        Command? command = null;
        if (CommandLine.IsCommand(e.Args))
        {
            command = CommandLine.Parse(e.Args, out var usageError);
            if (command is null)
            {
                CommandChannel.Print(usageError);
                Shutdown((int)ExitCode.Usage);
                return;
            }
        }

        if (command is not null)
        {
            var code = CommandChannel.Send(command.ToArgs());
            if (CommandLine.Describe(code, command) is { } message) CommandChannel.Print(message);
            Shutdown((int)code);
            return;
        }

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
        _media = new MediaSessionService();
        _dispatcher = new ActionDispatcher(_audio, _media, new ForegroundProcess(), _osd);
        _dispatcher.CycleDeviceIds = _settings.CycleDeviceIds;
        _tray = new TrayIcon();
        _tray.DefaultDeviceName = () => _audio.GetDevices().FirstOrDefault(d => d.IsDefault)?.Name;
        _tray.SetShortcuts(ShortcutLines(_settings));
        _tray.ProfileNames = () => _settings.Profiles.Select(p => p.Name).ToList();
        _tray.ProfileRequested += (_, name) => ApplyProfile(name);
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

        _muteAll = new MuteAll(_audio);
        _routing = new RoutingApplier(new AudioPolicyRouter(), () => _settings.CompetitiveMode);
        _enforcer = new LimitEnforcer(_audio, () => _settings.CompetitiveMode);
        _profileRetry.Tick += (_, _) =>
        {
            _profileRetry.Stop();
            _retry.Run(_audio, () => _settings.CompetitiveMode);
        };
        _routeTimer.Tick += (_, _) =>
        {
            _routeTimer.Stop();
            _routing.Apply(_settings.RoutingRules, _audio.GetDevices().Select(d => d.Id).ToList(), _audio.GetProcessIds);
        };
        _priorityTimer.Tick += (_, _) =>
        {
            _priorityTimer.Stop();
            ApplyDevicePriorityNow();
        };
        var ledger = new DuckLedger(_audio); // shared: levels always come back to what the user had
        _ducking = new DuckingEngine(_audio, () => _settings.Ducking, ledger, blocked: () => _settings.CompetitiveMode);
        _duckTimer.Tick += (_, _) => _ducking.Tick(Environment.TickCount64);
        _streamer = new DuckingEngine(_audio, () => _settings.Streamer.ToDucking(), ledger, () => _micMeter?.Peak(), () => _settings.CompetitiveMode);
        _streamTimer.Tick += (_, _) => _streamer.Tick(Environment.TickCount64);
        _limitTimer.Tick += (_, _) => { _limitTimer.Stop(); _enforcer.Enforce(); };
        _runner = new CommandRunner(_audio, () => _settings.CompetitiveMode, on => SetCompetitive(on, notify: true))
        {
            ToggleMuteAll = ToggleMuteAll,
            ApplyProfile = ApplyProfile,
            ToggleMic = ToggleMic,
        };
        _commandWindow = CommandChannel.CreateServer(args => CommandLine.Parse(args, out _) is { } c ? _runner.Run(c) : ExitCode.Usage);

        _freeHotkeys = new GlobalHotkeySet();
        _freeHotkeys.Pressed += entry => OnMediaAction(entry.Binding, entry.Action);
        _actionHotkeys.Pressed += id => Dispatcher.BeginInvoke(() => OnActionHotkey(id));
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
        if (on) { _hook!.Dispose(); _muteAll.Restore(); _profileRetry.Stop(); _retry.Clear(); }
        else InstallHook();
        ApplyHotkeys();
        // Start the session list over: drops process handles when entering the mode, rediscovers names and icons when leaving it.
        if (runtimeChange) _audio.Reset();
        ApplyLimits();
        ApplyRouting();
        ApplyDevicePriority();
        ApplyMicrophone();
        ApplyDucking();
        ApplyStreamer();
    }

    /// <summary>
    /// Streamer mode watches the peak level of the default microphone: the meter and its 100 ms timer exist only while the
    /// mode is on, an app is ticked and competitive mode is off; otherwise the lowered apps are restored and nothing runs.
    /// </summary>
    private void ApplyStreamer()
    {
        if (_settings.Streamer.Active && !_settings.CompetitiveMode)
        {
            _micMeter ??= new MicrophoneMeter();
            if (!_streamTimer.IsEnabled) _streamTimer.Start();
            return;
        }
        _streamTimer.Stop();
        _streamer.Restore();
        _micMeter?.Dispose();
        _micMeter = null;
    }

    /// <summary>
    /// Ducking watches the peak level of the voice apps, which needs a timer: it runs only while ducking is on, competitive
    /// mode is off and a voice app has a session. Session events re-check that; when it stops, lowered apps come back.
    /// </summary>
    private void ApplyDucking()
    {
        _audio.SessionsChanged -= OnDuckingEvent;
        var cfg = _settings.Ducking;
        if (!cfg.Enabled || _settings.CompetitiveMode)
        {
            StopDucking();
            return;
        }
        _audio.SessionsChanged += OnDuckingEvent;
        UpdateDuckTimer();
    }

    private void StopDucking()
    {
        _duckTimer.Stop();
        _ducking.Restore();
    }

    private void UpdateDuckTimer()
    {
        if (_settings.CompetitiveMode || !_settings.Ducking.Enabled) { StopDucking(); return; } // a queued call from before the switch
        var voiceRunning = _settings.Ducking.AppList.Any(a => a.Voice && _audio.GetProcessIds(a.ExeName).Count > 0);
        if (voiceRunning) { if (!_duckTimer.IsEnabled) _duckTimer.Start(); }
        else StopDucking();
    }

    // Any thread: re-check on the UI thread whether a voice app still has a session.
    private void OnDuckingEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateDuckTimer);

    /// <summary>
    /// The microphone is opened only while a microphone feature is configured (the shortcut or the mixer section) and
    /// competitive mode is off; it then follows the microphone through Windows notifications (tray icon, mixer).
    /// </summary>
    private void ApplyMicrophone()
    {
        var wanted = !_settings.CompetitiveMode && (!string.IsNullOrWhiteSpace(_settings.MicMuteHotkey) || _settings.MicrophoneInMixer);
        if (!wanted)
        {
            if (_mic is not null) { _mic.Changed -= OnMicChanged; _mic.Dispose(); _mic = null; }
            _tray?.SetMicMuted(false);
            return;
        }
        if (_mic is null)
        {
            _mic = new MicrophoneService();
            _mic.Changed += OnMicChanged;
        }
        _tray?.SetMicMuted(_mic.Available && _mic.Muted);
    }

    // Any thread (Windows notification): hop to the UI thread to read the microphone.
    private void OnMicChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_mic is null) return;
        _tray?.SetMicMuted(_mic.Available && _mic.Muted);
        _flyout?.NotifyMicrophoneChanged();
    });

    /// <summary>Mutes or unmutes the default microphone; false when there is none. Works without the observer too (one-shot).</summary>
    private bool ToggleMic()
    {
        var temporary = _mic is null ? new MicrophoneService() : null;
        try
        {
            var muted = MicrophoneToggle.Toggle((IMicrophone?)_mic ?? temporary!);
            if (muted is null) return false;
            _osd.Show(null, (_mic ?? temporary)!.Name, muted.Value ? "Microphone muted" : "Microphone unmuted");
            _tray?.SetMicMuted(_mic is not null && muted.Value);
            return true;
        }
        finally { temporary?.Dispose(); }
    }

    /// <summary>
    /// The output priority list is evaluated when devices come and go. The observer runs only while the list is on and
    /// not empty, and competitive mode is off; it starts with one evaluation.
    /// </summary>
    private void ApplyDevicePriority()
    {
        _audio.SessionsChanged -= OnPriorityEvent;
        _priorityTimer.Stop();
        _devicePriority.Reset();
        if (!_settings.DevicePriorityEnabled || _settings.DevicePriority.Count == 0 || _settings.CompetitiveMode) return;
        _audio.SessionsChanged += OnPriorityEvent;
        _priorityTimer.Start();
    }

    private void ApplyDevicePriorityNow()
    {
        var pick = _devicePriority.Apply(_audio, _settings.DevicePriority.Select(d => d.Id).ToList(), () => _settings.CompetitiveMode);
        if (pick is not null && _audio.GetDevices().FirstOrDefault(d => d.Id == pick)?.Name is { } name)
            _osd.Show(null, name, "Default output (priority list)");
    }

    // Any thread: wakes the UI thread, which evaluates once the burst of events is over.
    private void OnPriorityEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!_settings.CompetitiveMode && !_priorityTimer.IsEnabled) _priorityTimer.Start(); });

    /// <summary>
    /// The routing rules are applied when an app's session appears. The observer runs only while there is a rule and
    /// competitive mode is off; it starts with one pass over the running apps.
    /// </summary>
    private void ApplyRouting()
    {
        _audio.SessionsChanged -= OnRoutingEvent;
        _routeTimer.Stop();
        _routing.Reset();
        if (_settings.RoutingRules.Count == 0 || _settings.CompetitiveMode) return;
        _audio.SessionsChanged += OnRoutingEvent;
        _routeTimer.Start();
    }

    // Any thread: wakes the UI thread, which routes once the burst of events is over.
    private void OnRoutingEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!_settings.CompetitiveMode && !_routeTimer.IsEnabled) _routeTimer.Start(); });

    /// <summary>
    /// Hands the maximum volumes to the audio service (sliders, shortcuts and commands use them at once). The watch that
    /// brings an app back under its maximum runs only while there is one and competitive mode is off.
    /// </summary>
    private void ApplyLimits()
    {
        _audio.Limits.Set(_settings.MaxVolumes);
        _audio.VolumesChanged -= OnLimitEvent;
        _audio.SessionsChanged -= OnLimitEvent;
        _limitTimer.Stop();
        if (!_audio.Limits.Any || _settings.CompetitiveMode) return;
        _audio.VolumesChanged += OnLimitEvent;
        _audio.SessionsChanged += OnLimitEvent;
        _enforcer.Enforce();
    }

    // Any thread: only wakes the UI thread, which re-checks once the burst of events is over.
    private void OnLimitEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!_settings.CompetitiveMode && !_limitTimer.IsEnabled) _limitTimer.Start(); });

    /// <summary>The bindings and key remaps become what the keyboard hook decides with (read on the hook thread).</summary>
    private void RebuildDecider()
    {
        _resolver = new BindingResolver(_settings.Bindings);
        var resolver = _resolver;
        _decider = new KeyDecider(KeyRemapMap.From(_settings.KeyRemaps.Select(r => (r.Action, r.Key))), mods => resolver.Resolve(mods));
    }

    private static IReadOnlyList<string> ShortcutLines(AppSettings s) =>
        ShortcutSummary.Lines(s.Bindings).Concat(ShortcutSummary.RemapLines(s.KeyRemaps)).Concat(ShortcutSummary.FreeLines(s.FreeHotkeys)).ToList();

    /// <summary>Mutes every app, or restores them; false when nothing is playing. The OSD (when on) says which.</summary>
    private bool ToggleMuteAll()
    {
        _muteAll.Excluded = _settings.HiddenApps;
        var result = _muteAll.Toggle();
        if (result is not null) _osd.Show(null, "Mute everything", result.Value ? "All apps muted" : "Restored");
        return result is not null;
    }

    private void OnActionHotkey(string id)
    {
        if (_settings.CompetitiveMode) return;
        if (id == ActionIds.MuteAll) ToggleMuteAll();
        else if (id == ActionIds.Mic) ToggleMic();
        else if (id.StartsWith(ActionIds.ProfilePrefix, StringComparison.Ordinal)) ApplyProfile(id[ActionIds.ProfilePrefix.Length..]);
    }

    /// <summary>Applies a profile by name (tray menu, shortcut, command line); false when there is none. The OSD (when on) says what it did.</summary>
    private bool ApplyProfile(string name)
    {
        if (VolumeProfile.Find(_settings.Profiles, name) is not { } profile) return false;
        var r = ProfileApplier.Apply(profile, _audio);
        var total = r.Applied + r.Missing;
        _osd.Show(null, "Profile: " + profile.Name,
            total == 0 ? (r.MasterSet ? "Master volume set" : "Empty profile") : r.Missing == 0 ? $"{r.Applied} app(s) set" : $"{r.Applied} of {total} apps set (others not playing)");
        return true;
    }

    /// <summary>Registers every RegisterHotKey shortcut (none while competitive mode is on).</summary>
    private void ApplyHotkeys()
    {
        ApplyFreeHotkeys();
        var items = new List<(Hotkey, string)>();
        if (!_settings.CompetitiveMode)
        {
            if (Hotkey.TryParse(_settings.MuteAllHotkey, out var muteAll)) items.Add((muteAll, ActionIds.MuteAll));
            if (Hotkey.TryParse(_settings.MicMuteHotkey, out var micChord)) items.Add((micChord, ActionIds.Mic));
            foreach (var p in _settings.Profiles)
                if (Hotkey.TryParse(p.Hotkey, out var chord)) items.Add((chord, ActionIds.ProfilePrefix + p.Name));
        }
        var failed = _actionHotkeys.Apply(items);
        if (failed.Count > 0)
            _tray?.Notify("Sonic Screwdriver", $"Shortcut {string.Join(", ", failed)} is already used by Windows or another program and was not registered.");
    }

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
        var autoAny = _settings.AutoPrograms.Any(p => p.Enabled);
        // Profiles that start with a program use the same snapshot; never while competitive mode is on (no lookups).
        var any = autoAny || (!_settings.CompetitiveMode && ProfileTriggers.Any(_settings.Profiles));
        var running = any ? ProcessNames.Running() : new HashSet<string>();
        var program = AutoCompetitive.FirstRunning(_settings.AutoPrograms, running);
        if (_autoMode.Evaluate(program is not null, _settings.CompetitiveMode) is { } on)
        {
            _settings.CompetitiveMode = on; // not saved: it follows the program, not a preference
            ApplyMode();
            _tray?.Notify("Competitive mode: " + (on ? "on" : "off"),
                on ? $"Turned on because {program} is running." : "The program has exited.");
        }
        if (_settings.CompetitiveMode || !ProfileTriggers.Any(_settings.Profiles)) _profileTriggers.Reset();
        else
            foreach (var profile in _profileTriggers.Started(_settings.Profiles, running))
            {
                // An app often opens its audio session a few seconds after it starts: apply again 10 s later.
                if (ApplyProfile(profile.Name)) { _retry.Add(profile); _profileRetry.Stop(); _profileRetry.Start(); }
            }
        _autoTimer.IsEnabled = autoAny || (!_settings.CompetitiveMode && ProfileTriggers.Any(_settings.Profiles));
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
            _flyout = new FlyoutWindow(_audio, () => _settings, () => { TrySave(); }, _media,
                () => _settings.MicrophoneInMixer && !_settings.CompetitiveMode ? _mic : null);
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
        _settingsWindow = new SettingsWindow(_settings, _audio, h => _actionHotkeys.Owns(h) || _freeHotkeys.IsAvailable(h));
        _settingsWindow.Saved += s =>
        {
            if (!_settingsWindow.CompetitiveModeEdited) s.CompetitiveMode = _settings.CompetitiveMode; // keep a toggle made while the window was open
            var modeChanged = s.CompetitiveMode != _settings.CompetitiveMode;
            if (_settingsWindow!.CompetitiveModeEdited) _autoMode.UserChanged(s.CompetitiveMode);
            s.PinnedApps = _settings.PinnedApps; // changed from the mixer, not from this window
            s.HiddenApps = _settings.HiddenApps.Except(_settingsWindow.ShownAgain, StringComparer.OrdinalIgnoreCase).ToList();
            _settings = s;
            RebuildDecider();
            _dispatcher.CycleDeviceIds = s.CycleDeviceIds;
            _tray?.SetShortcuts(ShortcutLines(s));
            if (modeChanged) ApplyMode();
            else { ApplyHotkeys(); ApplyLimits(); ApplyRouting(); ApplyDevicePriority(); ApplyMicrophone(); ApplyDucking(); ApplyStreamer(); }
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
        _limitTimer.Stop();
        _routeTimer.Stop();
        _priorityTimer.Stop();
        _profileRetry.Stop();
        _duckTimer.Stop();
        _streamTimer.Stop();
        // Every lowered or muted app goes back to what the user had; one failure must not skip the others.
        ShutdownSteps.RunAll(() => _ducking?.Restore(), () => _streamer?.Restore(), () => _muteAll?.Restore());
        _micMeter?.Dispose();
        _commandWindow?.Dispose();
        _mic?.Dispose();
        _hook?.Dispose();
        _modeHotkey?.Dispose();
        _freeHotkeys?.Dispose();
        _actionHotkeys.Dispose();
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
