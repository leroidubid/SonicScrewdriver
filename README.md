# Sonic Screwdriver

A lightweight per-app volume mixer for Windows 10/11 that lives in the notification area, with
**modifier + media key shortcuts** that act on one specific app instead of the whole system.

Press `Shift + Vol−` to turn Spotify down while the game keeps its volume. `Ctrl + Mute` silences
whatever window is in front. `Alt + Vol+` switches your headset to the speakers.

<p align="center">
  <img src="docs/mixer.png" alt="The tray mixer: output devices and per-app sliders" width="300">&nbsp;&nbsp;&nbsp;
  <img src="docs/osd.png" alt="The on-screen display after a shortcut" width="241">
</p>

## Features

- **Per-app mixer.** Click the tray icon to see every output device, its volume, and the apps
  playing on it. Each app gets its own slider and mute button, and the list updates live.
- **Shortcuts on your media keys.** Bind a modifier combination (Ctrl, Alt, Shift, Win, or any mix)
  to a target. The same combination then drives Vol+, Vol−, Mute, Play/Pause, Next and Previous:
  - **a specific app** (`spotify.exe`, `discord.exe`, …): volume, mute and media control for that app only;
  - **the foreground app**: whatever window has focus;
  - **output devices**: Vol+ / Vol− cycle the default output through the devices you choose.
- **Remap the media keys.** No media keys on your keyboard (compact keyboards, laptops where they sit behind `Fn`)?
  In the settings, choose which key acts as Vol+, Vol−, Mute, Play/Pause, Next and Previous (for example F10, F11,
  F12). A remapped key works exactly like the real one: `Shift + F11` turns down the app bound to Shift, a bare
  `F11` lowers the master volume. A remapped key loses its usual job while the app runs, and the remaps are off in
  competitive mode (they need the keyboard hook).
- **Free shortcuts.** Not limited to media keys: record any combination (Ctrl, Alt, Shift or Win plus any key, for
  example `Ctrl + Alt + F8`) and choose what it does (Vol+, Vol−, Mute, Play/Pause, Next, Previous), to which app (a
  specific app, the foreground app or the output devices) and the volume step. These use `RegisterHotKey`: Windows
  matches the combination itself, so there is no extra keyboard hook, and the keys of a combination are taken from
  other programs only while it is registered. A combination already used by Windows or another program is refused
  with a message in the settings. Off in competitive mode.
- **Media keys stay normal.** A media key without a modifier, or with an unbound combination, keeps
  its usual Windows behavior.
- **On-screen display.** A small, non-intrusive overlay shows the app icon, its name and the new level.
- **Device control.** Set the default output from the mixer, or right-click an app to send it to
  another output.
- **Pin or hide apps.** Right-click an app in the mixer to **pin it to the top** of its device list (pinned apps keep
  a stable order) or **hide it from the mixer**. A hidden app keeps working with your shortcuts and its volume is not
  touched; the **Hidden apps** card in the settings lists them with a **Show again** button.
- **Mouse wheel.** Scroll over the tray icon to change the master volume, or over a mixer row to
  change that app.
- **At a glance.** The tray tooltip and the right-click menu show the current output device and your shortcuts. The
  menu is drawn like the mixer (same theme, above the taskbar, closes on Escape or a click elsewhere, usable with the
  arrow keys) and holds Settings, the Competitive mode switch and Quit.
- **Competitive mode.** One switch turns off everything an anti-cheat could flag (shortcuts, overlay, process
  lookups) for online games, with an optional shortcut to flip it. See [Games and anti-cheat](#games-and-anti-cheat).
- **Light.** Around 20 MB of RAM and no CPU at idle; everything is event driven (the only timer is the optional
  automatic competitive mode, a process-name check every 5 seconds, and only if you list a program).

## Settings

Right-click the tray icon, then **Settings**. The window has one card per topic:

- **Shortcuts**: each row is a modifier combination, the app it controls (a specific app, the foreground app or
  the output devices) and the volume step.
- **Free shortcuts**: any key combination bound to an action (Vol+, Vol−, Mute, Play/Pause, Next, Previous), a target
  and a step. Duplicate combinations and combinations that Windows or another program already owns are refused.
- **Output devices in the cycle**, and **Media keys** to choose which physical keys act as Vol+, Vol-, Mute, Play,
  Next and Previous.
- **Hidden apps**: the apps hidden from the mixer, each with a **Show again** button.
- **Competitive mode**: the switch, a shortcut to turn it on or off, and the games that turn it on
  automatically. While it is on, the shortcut options are greyed out.
- **General**: Start with Windows.

<p align="center">
  <img src="docs/settings.png" alt="Settings: shortcuts, output devices and media keys" width="290">&nbsp;
  <img src="docs/settings-mode.png" alt="Settings: competitive mode, its shortcut and the automatic list" width="290">&nbsp;
  <img src="docs/settings-competitive.png" alt="Settings with Competitive mode on: the shortcut options are greyed out" width="290">
</p>

## Why

I wanted to change the volume of several apps quickly while a game was running full screen: turn Discord up,
the music down, without leaving the game. I had been using hotkey scripts, but they were a pain to maintain, and I
could not find an existing tool that did what I wanted. So I built one: a small, light mixer whose shortcuts live on
the media keys and act on the app you choose.

## Install

1. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if you don't have it
   (the installer checks and links to it).
2. From the Releases page, download either:
   - `SonicScrewdriver-Setup-x.y.z.exe`: per-user install, no admin rights, Start menu shortcut,
     optional start with Windows, clean uninstall;
   - or `SonicScrewdriver.exe`: portable, just run it.
3. Right-click the tray icon, choose **Settings**, and add your shortcuts.

Windows SmartScreen may warn on first run because the files are not code-signed:
click **More info**, then **Run anyway**.

The default shortcut is `Ctrl + media keys` on the foreground app. Settings are stored in
`%APPDATA%\SonicScrewdriver\settings.json`.

## Limitations

- **Media control:** Play/Pause, Next and Previous only reach apps that register with Windows media
  controls (Spotify, browsers, most players).
- **Browsers:** all tabs and installed web apps of a browser share a single volume, because the browser
  plays all their audio through one process.
- **Administrator windows:** when an elevated window has focus, Windows does not pass keys to the app
  unless it runs elevated too.
- **Undocumented APIs:** changing the default device and routing an app to an output use undocumented
  Windows interfaces, the same ones EarTrumpet uses.

## Games and anti-cheat

Sonic Screwdriver is not a cheat: it installs no driver, injects nothing into other programs and never reads their
memory. Even so, anti-cheat systems are opaque, and a global keyboard hook can look suspicious to some of them, so
compatibility cannot be guaranteed.

For online games with strict anti-cheat (Vanguard, FACEIT, EasyAntiCheat, BattlEye), turn on **Competitive mode**
(tray menu, or Settings) or quit the app first. In that mode:

| Switched off | What you lose |
|---|---|
| The keyboard hook | All modifier + media key shortcuts, remapped media keys, and output device cycling |
| The on-screen display | The overlay shown after a shortcut |
| Mouse wheel on the tray icon | Master volume with the wheel |
| Lookups of other programs | App icons and friendly names (the exe name and a generic icon are shown instead) |

The mixer, the sliders, switching the default output and the physical media keys keep working. Competitive mode
lowers the risk but does not remove it: quitting the app is the safest choice.

**Automatic mode (off by default).** In the settings you can list the games to protect: while a ticked program runs,
competitive mode turns on by itself, and it turns off again when the program exits. A program is spotted by its exe
name in the Windows process list, read from one snapshot every 5 seconds: the process is never opened, so no handle
or access right touches the game. Nothing runs while the list has no ticked program. If you switch the mode yourself,
the automation steps aside: a mode you turned on stays on, and one you turned off stays off until the program has
exited. An automatic "on" is not saved as your preference.

**Toggle shortcut.** In the settings you can assign a key combination (for example `Ctrl+Alt+F9`) that switches
competitive mode on and off, with a short notification each time. It has to work while the keyboard hook is
uninstalled, so it does not use a hook: it is registered with the Windows `RegisterHotKey` function, where Windows
itself matches the combination and tells the app. The app never sees any other key. The trade-off: this is a
documented, ordinary API that many programs use and is much less intrusive than a hook, but it is still one
registration that stays active in competitive mode. If you want nothing at all, leave the shortcut empty (the default)
and use the tray menu, or quit the app.

## Security

- The keyboard hook only looks at the six media keys; no other key is read or stored. The app does not
  use the network and collects nothing.
- Releases are built by GitHub Actions from the tagged commit, scanned with Microsoft Defender and
  [VirusTotal](https://www.virustotal.com) (the report links are added to the release notes), and come
  with `SHA256SUMS.txt`.
- You can check that a download was built from this repository (needs the [GitHub CLI](https://cli.github.com)):

  ```bash
  gh attestation verify SonicScrewdriver-Setup-x.y.z.exe --repo leroidubid/SonicScrewdriver
  ```

- **Antivirus false positives.** The files are not code-signed, and a few machine-learning engines on VirusTotal
  (for example SecureAge and Acronis) flagged an early build as suspicious, while Microsoft Defender and the
  other engines found nothing. These engines react to the API calls any keyboard-shortcut tool needs (a keyboard
  hook, key injection, start with Windows), not to anything harmful. To check a download yourself, compare its
  hash with `SHA256SUMS.txt`, verify the attestation above, or read and build the source.

## Build

Requires the .NET 8 SDK.

```bash
dotnet test
```

```bash
dotnet publish src/SonicScrewdriver -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -o publish
```

Installer (requires [Inno Setup 6](https://jrsoftware.org/isinfo.php), after publishing):

```bash
iscc installer/SonicScrewdriver.iss /DAppVersion=1.0.0
```

`tools/e2e-test.ps1` runs an end-to-end test against the running app (synthetic key presses).

## License

[MIT + Commons Clause](LICENSE): free to use, modify and share, but **not to sell**.

- You can use it, at home or at work, modify it, fork it, and share it for free.
- You cannot sell it, sell a modified version, or sell a service whose value comes mainly from it
  (paid hosting, paid support, a paid product built around it) without my written permission.
- Want to use it commercially? Contact me through my [GitHub profile](https://github.com/leroidubid).
- This is a source-available license, not an OSI-approved open-source one.

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

The name nods to a certain time traveller's favourite tool. This project is not affiliated with or
endorsed by the BBC.
