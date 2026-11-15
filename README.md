<p align="center"><img src="tools/icon-preview.png" alt="Sonic Screwdriver icon" width="360"></p>

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
- **Media keys stay normal.** A media key without a modifier, or with an unbound combination, keeps
  its usual Windows behavior.
- **On-screen display.** A small, non-intrusive overlay shows the app icon, its name and the new level.
- **Device control.** Set the default output from the mixer, or right-click an app to send it to
  another output.
- **Mouse wheel.** Scroll over the tray icon to change the master volume, or over a mixer row to
  change that app.
- **At a glance.** The tray tooltip and menu show the current output device and your shortcuts.
- **Light.** Around 20 MB of RAM and no CPU at idle; no polling, everything is event driven.

## Install

1. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if you don't have it
   (the installer checks and links to it).
2. From the Releases page, download either:
   - `SonicScrewdriver-Setup-x.y.z.exe`: per-user install, no admin rights, Start menu shortcut,
     optional start with Windows, clean uninstall;
   - or `SonicScrewdriver.exe`: portable, just run it.
3. Right-click the tray icon, choose **Shortcut settings**, and add your shortcuts.

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
