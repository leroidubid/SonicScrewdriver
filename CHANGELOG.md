# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- **License:** from MIT + Commons Clause to GPL-3.0 (free software). A copy you pass on, modified or not, must come with
  its source under the same license. The contributor terms no longer give the maintainer a right to relicense. Versions up
  to 1.4.0 stay under their original license.

## [1.4.0] - 2026-11-23

### Added

- **Updates:** the Updates card shows the release notes of the new version before you install it (plain text, taken from the
  GitHub release), and a "Skip this version" button. A skipped version is no longer mentioned by the startup check; the
  manual check still offers it.
- **Updates:** when the startup check finds a newer release, the tray menu gets an "Update x.y.z available" entry that opens
  Settings, where the update is installed.

## [1.3.1] - 2026-11-22

### Fixed

- **Updates:** a portable copy (an exe not set up by the installer) is now replaced in place: the checked
  `SonicScrewdriver.exe` of the release is downloaded, the running exe is renamed to `.old`, the new one takes its name and
  starts once the old process has ended (`--updated-from <pid>`, which also deletes the `.old` file). Before, the button ran
  the installer, which installed a second copy and left the portable exe on the old version. An exe in a folder that cannot
  be written reports it and stays as it was.

## [1.3.0] - 2026-11-22

### Added

- **Updates:** Settings, General, gets an Updates card. "Check for updates" asks GitHub for the latest stable release;
  "Install" downloads its installer, checks the SHA256 against the `SHA256SUMS.txt` published with the release, closes
  the app, installs silently and starts the app again. "Check at startup" (off by default) makes one request 30 seconds
  after the start and shows a notification when a release is newer. Nothing is requested in competitive mode. The only
  data GitHub sees is the IP address and the fixed name `SonicScrewdriver`.

## [1.2.1] - 2026-11-22

### Added

- **Error log:** unexpected errors are written to `%APPDATA%\SonicScrewdriver\log.txt` (time, message and stack; about
  100 KB at most, identical repeats written once a minute). It stays on the machine and is meant for bug reports.

### Fixed

- **Media sessions:** a media session that disappears while the app reads or subscribes to the list can no longer end the
  process; the same protection now covers the audio notifications and the keyboard hook.

### Changed

- Internal clean-ups: one hotkey registry instead of three, one device read for the output priority list, shared
  ducking and streamer numbers, safer release workflow (version through the environment, tag must be on main), a
  workflow lint script (`tools/check-workflows.py`) and new tests.
- **Chocolatey:** a package generator (`installer/make-choco-package.ps1`) and a workflow that builds the package of each
  stable release from the published installer and its SHA256, and pushes it when the `CHOCO_API_KEY` secret is set.

## [1.2.0] - 2026-11-17

### Added

- **Website:** https://leroidubid.github.io/SonicScrewdriver/ presents the features, the download (latest release), the
  command line, competitive mode and the tested games and systems, the security notes, the changelog and an FAQ. Plain
  HTML and CSS, no tracker and no third-party request. The README gains the tables of tested games (with whether a game
  needs competitive mode) and of tested Windows versions.

### Fixed

- **Competitive mode:** automatic ducking, streamer mode, maximum volumes, remembered routing, the output priority list and
  the profile retry can no longer act after the mode is switched on (a callback queued just before the switch used to
  restart them, and ducked apps stayed lowered).
- **Exit:** quitting with "Mute everything", ducking or streamer mode active restores every app to what it was; one
  failing step no longer skips the others.
- **Ducking and streamer mode:** a level you set by hand while an app is lowered is kept; an app that quit is dropped; a
  failed restore is retried.
- **Mute everything:** an app with sessions on several outputs gets back the mute state each output had.
- **Audio devices:** no COM leak when a device tracker fails to build, and a tracker is rebuilt after a quick unplug and
  replug. The tray no longer reads the devices on every mouse move in competitive mode. A late old keyboard-hook thread
  can no longer unhook a newer one.
- **Settings window:** a second Save no longer turns competitive mode off (or on) by itself after a shortcut toggled it: the
  box follows the live mode and only your own change of the box counts.
- **Competitive mode:** after switching it off by hand, the profiles that start with a program are watched again. A
  microphone muted by the shortcut, the command line or the mixer is unmuted when the mode starts (it is not muted again
  afterwards), instead of staying muted with no icon and no shortcut.
- **Settings:** a failing "Start with Windows" registry write no longer prevents `settings.json` from being saved; both
  failures are reported separately.
- **Ducking and streamer mode:** an app that has no audio session at the moment its level is given back is restored when it
  comes back (for up to 5 minutes) instead of keeping the lowered level for good.
- **Command line:** a command that reached a busy instance after the 5 s timeout is no longer run later, so the "no answer"
  (exit code 5) is truthful and a retry cannot toggle twice.
- **Mixer:** opens on the monitor of the tray icon, inside its work area and at its scaling, not always at the bottom right of
  the primary screen.
- **Audio devices:** a device notification arriving during a rebuild of the trackers can no longer be lost.
- **Installer:** finds the .NET 8 Desktop Runtime through the registry and in the per-user, Windows on Arm and `DOTNET_ROOT`
  locations too.
- **Release workflow:** `wingetcreate` is downloaded from a pinned release and checked against its SHA256, Inno Setup is
  installed at a pinned version, and the winget job has no workflow token permissions.

## [1.1.0] - 2026-11-16

### Added

- **Command line:** `SonicScrewdriver.exe --set <app> <0-100>`, `--mute`, `--unmute`, `--toggle-mute`, `--profile <name>`,
  `--mute-all`, `--mic-mute` and `--toggle-competitive`. A second launch hands the command to the running instance
  (WM_COPYDATA to a hidden message-only window: no pipe, no service) and exits with a code (0 done, 1 usage, 2 not
  running, 3 not found, 4 refused in competitive mode, 5 no answer) and a message on the parent console. Documented
  for Stream Deck, Touch Portal and AutoHotkey.
- **Mute everything:** one shortcut mutes every app (per-app mute: the Windows master and the levels are untouched;
  hidden apps are left out), the next press restores each previous mute state exactly.
- **Maximum volume per app:** a cap (1-100 %) applied to the slider, the shortcuts, the commands and the profiles; the
  slider snaps back; an app that goes above its cap by itself is brought back (a watch that runs only while a cap exists
  and not in competitive mode).
- **Volume profiles:** named sets of app volumes with an optional master volume, an optional shortcut and an optional
  program that applies the profile when it starts (same process-name snapshot as the automatic competitive mode).
  Listed in the tray menu, editable in the settings (with "Add running apps").
- **Now playing in the mixer** (off by default): title, artist and Previous / Play-Pause / Next buttons in each row,
  from the Windows media sessions, read only while the mixer is open.
- **Remembered routing:** "this app always plays on this output", applied when the app's session appears; a device that
  is not plugged in is skipped. Off in competitive mode.
- **Output device priority list** (off by default): the default output becomes the first plugged-in device of an ordered
  list when devices come and go; an output chosen by hand is kept until the top choice changes. Off in competitive mode.
- **Microphone:** a shortcut mutes and unmutes the default microphone (OSD, grey tray icon while muted) and the mixer can
  show its volume and mute. Follows Windows notifications only while one of these is on; off in competitive mode.
- **Automatic ducking** (off by default): a voice app's session peak level lowers the apps marked as ducked to a chosen
  percentage of their level, with attack and release delays, restores the exact levels, and respects a slider moved
  meanwhile. The meter runs only while a voice app has a session and not in competitive mode.
- **Streamer mode** (off by default): the reverse of ducking. While you speak into the default microphone (its peak level
  above a threshold, 5 % by default, with attack and release delays) the apps you ticked go down to a percentage of
  their level (30 % by default) and come back to the exact previous levels; a slider moved meanwhile is respected. It
  shares the ducking engine and a common ledger of levels, so with both active nothing is lowered twice and everything
  comes back to what you had. The microphone meter and its 100 ms timer exist only while the mode is on and an app is
  ticked; microphone changes (unplugged, new default) are handled; off in competitive mode.
- **Settings in three tabs** (Shortcuts, Audio, General) with a flat tab style, keyboard navigation (Ctrl+Tab, arrows),
  each tab scrolling on its own, and a red "Competitive mode" label on the
  tabs that have greyed options.
- **Save and Save & exit** buttons: Save applies and keeps the window open ("Saved" note, Cancel becomes Close), Save & exit
  applies and closes (Enter). Errors keep the window open.
- Hand-edited settings for all of these are validated and sanitized like the existing ones.

### Changed

- **Mixer:** the device, each app and the "now playing" line have a very light background of their own (three tints), so
  the names are easier to tell apart. The settings window greys out what competitive mode switches off and marks each
  card and tab concerned with a red "Competitive mode" label.

## [1.0.0] - 2026-11-16

First public release.

### Added

- **Mixer:** a tray-icon mixer that lists every active output device with its volume and mute, plus
  each app playing on it (icon, name, slider, mute). It updates live from Windows audio events.
- **Media-key shortcuts:** bind a modifier combination (Ctrl, Alt, Shift, Win) to a target; it then
  drives Vol+, Vol−, Mute, Play/Pause, Next and Previous.
  - **A specific app** (e.g. `spotify.exe`): volume, mute and media control for that app only.
    Changing the volume of a muted app unmutes it.
  - **The foreground app.**
  - **Output devices:** Vol+ / Vol− cycle the default output through a chosen list of devices
    (all enabled devices by default).
- **Remap media keys:** choose which physical keys (F1–F24, Pause, Scroll Lock, Insert, Home, End, Page Up/Down,
  numpad) act as Vol+, Vol−, Mute, Play/Pause, Next and Previous. A remapped key resolves through the same shortcuts
  as the real key; without a shortcut it is replaced by the real media key (sent with `SendInput`, tagged so the hook
  ignores it). The tray tooltip and menu list the remaps. Off in competitive mode.
- **Free shortcuts:** bind any key combination (Ctrl, Alt, Shift or Win plus any key) to Vol+, Vol−, Mute,
  Play/Pause, Next or Previous on a specific app, the foreground app or the output devices, with its own volume step.
  They are registered with `RegisterHotKey` (no extra keyboard hook; Windows matches the combination), re-registered
  when the settings change, and unregistered in competitive mode. Duplicate combinations and combinations already owned
  by Windows or another program are refused with a message in the settings window, never a crash. Hand-edited settings
  are validated and sanitized, and the tray tooltip and menu list them.
- **Pass-through:** media keys without a modifier, or with an unbound combination, keep their normal
  Windows behavior.
- **On-screen display:** shows the app icon, its name and the new level, without stealing focus.
- **Device control:** set the default output device from the mixer, and route an app to another
  output (right-click it in the mixer).
- **Pin or hide apps:** right-click an app in the mixer for **Pin to top** (pinned apps stay first in their device
  list, in a stable order) or **Hide from the mixer**. Hidden apps are not listed but keep working with shortcuts, and
  their volume is not touched. Both lists are stored as exe names in the settings file (validated on load), and a
  **Hidden apps** card in the settings window lists the hidden apps with a **Show again** button.
- **Mouse wheel:** over the tray icon for the master volume, and over a mixer row for that app.
- **Tray tooltip and menu:** both show the current output device and the configured shortcuts. The right-click menu is
  a flyout in the same design as the mixer (Settings, Competitive mode, Quit) instead of the native Win32 menu: it opens
  inside the work area next to the taskbar, closes on Escape or an outside click, and works with the arrow keys.
- **Settings window:** apps are listed as "Name (exe)", with the output-device cycle list and the
  Start with Windows option. Settings are saved to `%APPDATA%\SonicScrewdriver\settings.json`;
  hand-edited files are sanitized on load and corrupt files are backed up.
- **Pixel-art tray icon:** the 16, 20, 24 and 32 px frames of the icon (what the notification area uses at 100 to
  200 % scale) are hand-placed pixel art, so the screwdriver stays readable; larger sizes keep the smooth drawing. The
  red competitive-mode icon now turns only the green emitter and sound arcs red, keeping the steel and grip neutral.
- **Settings window:** one card per topic, with modifier chips, switches, styled fields and a dark title bar.
- **Installer:** available in English, French, German, Spanish, Italian and Brazilian Portuguese. It is a per-user Inno Setup installer that needs no admin rights, with a Start menu
  shortcut, an optional start with Windows, a .NET 8 Desktop Runtime check and a clean uninstall.
  A portable single-file exe is also provided.
- **Competitive mode:** one switch (tray menu or settings) turns off the keyboard hook, the on-screen display, the
  mouse wheel on the tray icon and the lookups of other programs, so nothing can be mistaken for cheating software
  by an anti-cheat. The options it turns off are greyed out in the settings window.
- **Competitive mode shortcut:** an optional key combination (settings window) that switches competitive mode on and
  off, with a short notification. It uses `RegisterHotKey` instead of the keyboard hook, so it keeps working while the
  hook is uninstalled and no other key is ever seen.
- **Automatic competitive mode:** off by default. A list of programs (exe names, each with its own on/off tick) in the
  settings window turns competitive mode on while one of them runs and off when it exits, unless you switched it by
  hand. Detection reads the process list every 5 seconds without opening any process, and only while a program is ticked.

### Reliability

- **Keyboard hook:** runs on its own thread, so a busy UI can never make Windows drop it. Each key
  press is latched from key-down to key-up, so releasing a modifier mid-press is handled correctly.
  Alt and Win presses are masked so they do not open the menu bar or the Start menu.
- **Audio events:** audio sessions are tracked through Windows notifications instead of polling. The
  app recovers when output devices change or are unplugged, and drops the sessions of apps that
  have closed.
- **Startup:** a startup failure is reported instead of leaving an invisible process; a locked
  settings file falls back to defaults; save errors are reported.
- **Footprint:** about 20 MB of RAM and no CPU use at idle.
- **Mixer:** changing the slider of a muted app unmutes it, a slider being dragged is no longer moved by refreshes, plugging a device only touches that device, microphones are ignored, and an app session is never listed twice.
- **Play/Pause/Next:** when an app has several media sessions (a browser and its installed web apps), the one that is playing is used.
- **Minimal process access:** the app no longer opens other programs with the right to read their memory (which
  anti-cheat software strips and may log). It reads their path with the minimal right, and app names and icons come
  from the file on disk.

[1.2.0]: https://github.com/leroidubid/SonicScrewdriver/releases/tag/v1.2.0
[1.1.0]: https://github.com/leroidubid/SonicScrewdriver/releases/tag/v1.1.0
[1.0.0]: https://github.com/leroidubid/SonicScrewdriver/releases/tag/v1.0.0
