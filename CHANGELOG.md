# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-08

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
- **Pass-through:** media keys without a modifier, or with an unbound combination, keep their normal
  Windows behavior.
- **On-screen display:** shows the app icon, its name and the new level, without stealing focus.
- **Device control:** set the default output device from the mixer, and route an app to another
  output (right-click it in the mixer).
- **Mouse wheel:** over the tray icon for the master volume, and over a mixer row for that app.
- **Tray tooltip and menu:** both show the current output device and the configured shortcuts.
- **Settings window:** apps are listed as "Name (exe)", with the output-device cycle list and the
  Start with Windows option. Settings are saved to `%APPDATA%\SonicScrewdriver\settings.json`;
  hand-edited files are sanitized on load and corrupt files are backed up.
- **Installer:** per-user Inno Setup installer that needs no admin rights, with a Start menu
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
- **Minimal process access:** the app no longer opens other programs with the right to read their memory (which
  anti-cheat software strips and may log). It reads their path with the minimal right, and app names and icons come
  from the file on disk.

[1.0.0]: https://github.com/leroidubid/SonicScrewdriver/releases/tag/v1.0.0
