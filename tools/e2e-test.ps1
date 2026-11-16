# SonicScrewdriver end-to-end test: drives the real app with synthetic key presses.
# Uses a silent looping WAV in a child PowerShell as the "target app" (powershell.exe).
# Restores the master volume and removes the temporary settings at the end.
param(
    [string]$Exe = "$PSScriptRoot\..\src\SonicScrewdriver\bin\Debug\net8.0-windows10.0.19041.0\SonicScrewdriver.exe",
    # NAudio DLLs this script uses to observe volumes (single-file publishes embed them).
    [string]$NAudioDir = "$PSScriptRoot\..\src\SonicScrewdriver\bin\Debug\net8.0-windows10.0.19041.0"
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$binDir = (Resolve-Path $NAudioDir).Path
Add-Type -Path (Join-Path $binDir 'NAudio.Core.dll')
Add-Type -Path (Join-Path $binDir 'NAudio.Wasapi.dll')

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Kb {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    const uint KEYUP = 2, EXT = 1;
    // Media keys are extended keys; modifiers are sent as plain (left) keys. An extended Alt is AltGr = Ctrl+Alt.
    static uint Ext(byte vk) { return vk >= 0xA6 && vk <= 0xB7 ? EXT : 0u; }
    public static void Down(byte vk) { keybd_event(vk, 0, Ext(vk), UIntPtr.Zero); }
    public static void Up(byte vk)   { keybd_event(vk, 0, Ext(vk) | KEYUP, UIntPtr.Zero); }
    public static void Press(byte vk) { Down(vk); System.Threading.Thread.Sleep(30); Up(vk); }
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
"@

$VK_SHIFT = 0x10; $VK_CONTROL = 0x11
$VK_VOL_MUTE = 0xAD; $VK_VOL_DOWN = 0xAE; $VK_VOL_UP = 0xAF

$results = New-Object System.Collections.Generic.List[object]
function Check($name, $ok, $detail) {
    $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
}

function Device { (New-Object NAudio.CoreAudioApi.MMDeviceEnumerator).GetDefaultAudioEndpoint('Render', 'Multimedia') }
function TargetSession([int]$targetPid) {
    $mgr = (Device).AudioSessionManager
    $mgr.RefreshSessions()
    $s = $mgr.Sessions
    for ($i = 0; $i -lt $s.Count; $i++) { if ($s[$i].GetProcessID -eq $targetPid) { return $s[$i] } }
    return $null
}
function Master { (Device).AudioEndpointVolume }

# --- silent WAV player (target app) ---
$wav = Join-Path $env:TEMP 'mixkeys-silence.wav'
$sr = 8000; $n = $sr * 2
$bw = New-Object System.IO.BinaryWriter([System.IO.File]::Create($wav))
$bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $n)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$sr); $bw.Write([int]$sr); $bw.Write([int16]1); $bw.Write([int16]8)
$buf = [byte[]]::new($n); [Array]::Fill($buf, [byte]128)
$bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]$n); $bw.Write($buf); $bw.Close()
$player = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile', '-Command',
    "(New-Object System.Media.SoundPlayer '$wav').PlayLooping(); Start-Sleep 300"

# --- settings: Shift -> powershell.exe (step 10), Ctrl -> foreground ---
$settingsDir = Join-Path $env:APPDATA 'SonicScrewdriver'
$settingsPath = Join-Path $settingsDir 'settings.json'
$hadSettings = Test-Path $settingsPath
if ($hadSettings) { Copy-Item $settingsPath "$settingsPath.e2e-backup" -Force }
New-Item -ItemType Directory -Force $settingsDir | Out-Null
@'
{
  "Bindings": [
    { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 },
    { "Modifiers": "Ctrl",  "Target": { "Kind": "Foreground", "ExeName": null }, "StepPercent": 5 },
    { "Modifiers": "Alt",   "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 }
  ],
  "StartWithWindows": false
}
'@ | Set-Content $settingsPath -Encoding UTF8

$masterBefore = (Master).MasterVolumeLevelScalar
$masterMuteBefore = (Master).Mute
$app = $null
try {
    Start-Sleep -Milliseconds 1500
    $session = TargetSession $player.Id
    Check 'Target audio session exists' ($null -ne $session) "pid $($player.Id)"
    if ($null -eq $session) { throw 'no target session' }
    $session.SimpleAudioVolume.Volume = 0.8
    $session.SimpleAudioVolume.Mute = $false

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $app = Start-Process $Exe -PassThru
    Start-Sleep -Milliseconds 2500
    $app.Refresh()
    Check 'App starts and stays running' (-not $app.HasExited) "started in ~$($sw.ElapsedMilliseconds) ms (incl. wait)"

    # 1. Shift+Vol- lowers only the target app by 10 %
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 400
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    $m = (Master).MasterVolumeLevelScalar
    Check 'Shift+Vol- lowers target by step' ([math]::Abs($v - 0.7) -lt 0.011) ("target={0:N2} (expected 0.70)" -f $v)
    Check 'Shift+Vol- leaves master untouched' ([math]::Abs($m - $masterBefore) -lt 0.005) ("master {0:N2} -> {1:N2}" -f $masterBefore, $m)

    # 2. Shift+Vol+ raises it back
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_UP); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 400
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Shift+Vol+ raises target by step' ([math]::Abs($v - 0.8) -lt 0.011) ("target={0:N2} (expected 0.80)" -f $v)

    # 2b. Shift released mid-hold: the whole press stays with SonicScrewdriver (repeats act on target, master untouched)
    $m0 = (Master).MasterVolumeLevelScalar
    [Kb]::Down($VK_SHIFT); [Kb]::Down($VK_VOL_DOWN); Start-Sleep -Milliseconds 50; [Kb]::Up($VK_SHIFT)
    [Kb]::Down($VK_VOL_DOWN); [Kb]::Down($VK_VOL_DOWN); [Kb]::Up($VK_VOL_DOWN); Start-Sleep -Milliseconds 500
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    $m = (Master).MasterVolumeLevelScalar
    Check 'Modifier released mid-hold: repeats stay latched' ([math]::Abs($v - 0.5) -lt 0.011) ("target={0:N2} (expected 0.50)" -f $v)
    Check 'Modifier released mid-hold: master untouched' ([math]::Abs($m - $m0) -lt 0.005) ("master {0:N2} -> {1:N2}" -f $m0, $m)

    # 2c. Alt binding works (the injected mask key keeps the menu bar from grabbing focus)
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    [Kb]::Down(0x12); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up(0x12); Start-Sleep -Milliseconds 400
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Alt+Vol- lowers target by step' ([math]::Abs($v - 0.7) -lt 0.011) ("target={0:N2} (expected 0.70)" -f $v)
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8

    # 3. Auto-repeat clamp: 12 x Vol+ from 0.8 must cap at 1.0
    [Kb]::Down($VK_SHIFT); 1..12 | ForEach-Object { [Kb]::Press($VK_VOL_UP) }; [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 600
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Repeated Vol+ clamps at 100 %' ([math]::Abs($v - 1.0) -lt 0.001) ("target={0:N2}" -f $v)

    # 4. Shift+Mute toggles the target's mute, twice
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_MUTE); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 400
    $mu1 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_MUTE); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 400
    $mu2 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    Check 'Shift+Mute toggles target mute' ($mu1 -and -not $mu2) "after 1st=$mu1, after 2nd=$mu2"
    Check 'Shift+Mute leaves master mute' ((Master).Mute -eq $masterMuteBefore) "master mute=$((Master).Mute)"

    # 5. Unbound combo (Ctrl+Shift) passes through to Windows: master changes
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Down($VK_CONTROL); [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); [Kb]::Up($VK_CONTROL); Start-Sleep -Milliseconds 400
    $m = (Master).MasterVolumeLevelScalar
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Unbound Ctrl+Shift+Vol- passes to Windows' ($m -lt 0.5) ("master 0.50 -> {0:N2}" -f $m)
    Check 'Unbound combo does not touch target' ([math]::Abs($v - 1.0) -lt 0.001) ("target={0:N2}" -f $v)

    # 6. Bare Vol- passes through
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Press($VK_VOL_DOWN); Start-Sleep -Milliseconds 900
    $m = (Master).MasterVolumeLevelScalar
    Check 'Bare Vol- keeps default behavior' ($m -lt 0.5) ("master 0.50 -> {0:N2}" -f $m)

    # 7. Stress: 200 rapid Shift+Vol-/Vol+ presses, app stays responsive and alive
    $sw.Restart()
    [Kb]::Down($VK_SHIFT)
    1..100 | ForEach-Object { [Kb]::Down($VK_VOL_DOWN); [Kb]::Up($VK_VOL_DOWN); [Kb]::Down($VK_VOL_UP); [Kb]::Up($VK_VOL_UP) }
    [Kb]::Up($VK_SHIFT)
    Start-Sleep -Milliseconds 1000
    $app.Refresh()
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Stress 200 presses: app alive' (-not $app.HasExited) "took $($sw.ElapsedMilliseconds) ms to send"
    Check 'Stress: no stuck modifier / lost key-up' (([Math]::Abs($v - 1.0) -lt 0.001)) ("target={0:N2} (down/up pairs net zero, capped at 1.0)" -f $v)

    # 8. Footprint (after the OSD has hidden and the app is truly idle)
    Start-Sleep -Seconds 3
    $app.Refresh()
    $ws = [math]::Round($app.WorkingSet64 / 1MB, 1)
    $priv = [math]::Round($app.PrivateMemorySize64 / 1MB, 1)
    $cpu1 = $app.TotalProcessorTime; Start-Sleep -Seconds 3; $app.Refresh(); $cpu2 = $app.TotalProcessorTime
    $idleCpuMs = [math]::Round(($cpu2 - $cpu1).TotalMilliseconds)
    Check 'Idle CPU over 3 s' ($idleCpuMs -lt 50) "$idleCpuMs ms CPU"
    Check 'Memory footprint' ($priv -lt 150) "working set $ws MB, private $priv MB, threads $($app.Threads.Count)"

    # 9. Single instance: a 2nd launch exits quickly, 1st keeps running
    $second = Start-Process $Exe -PassThru
    $exited = $second.WaitForExit(5000)
    $app.Refresh()
    Check 'Second instance exits, first survives' ($exited -and -not $app.HasExited) "2nd exited=$exited"

    # 10. Locked settings file at startup: app runs with defaults instead of hanging invisibly
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    $lock = [IO.File]::Open($settingsPath, 'Open', 'ReadWrite', 'None')
    try {
        $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
        $alive = -not $app.HasExited
        # Defaults only bind Ctrl -> foreground, so Shift+Vol- must leave the target alone.
        (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
        [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 500
        $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
        Check 'Locked settings: app starts with defaults' ($alive -and [math]::Abs($v - 0.8) -lt 0.001) ("alive=$alive, Shift unbound: target={0:N2}" -f $v)
    } finally { $lock.Dispose() }

    # 11. Competitive mode: no keyboard hook, so Shift+Vol- is not intercepted (the master volume moves, the target does not)
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    @'
{
  "Bindings": [
    { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 }
  ],
  "StartWithWindows": false,
  "CompetitiveMode": true,
  "CompetitiveHotkey": "Ctrl+Alt+F9"
}
'@ | Set-Content $settingsPath -Encoding UTF8
    $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
    $alive = -not $app.HasExited
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 900
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    $m = (Master).MasterVolumeLevelScalar
    Check 'Competitive mode: shortcuts are not intercepted' ($alive -and [math]::Abs($v - 0.8) -lt 0.001 -and $m -lt 0.5) ("alive=$alive, target={0:N2}, master 0.50 -> {1:N2}" -f $v, $m)

    # 12. The competitive-mode shortcut (RegisterHotKey, no hook) works while the mode is on, and toggles both ways
    function ToggleMode { [Kb]::Down($VK_CONTROL); [Kb]::Down(0x12); [Kb]::Press(0x78); [Kb]::Up(0x12); [Kb]::Up($VK_CONTROL); Start-Sleep -Milliseconds 800 }
    ToggleMode   # on -> off: the keyboard hook comes back
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 500
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Mode shortcut: competitive off restores the shortcuts' ([math]::Abs($v - 0.7) -lt 0.011) ("target={0:N2} (expected 0.70)" -f $v)
    ToggleMode   # off -> on again
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 900
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    $m = (Master).MasterVolumeLevelScalar
    Check 'Mode shortcut: competitive on again drops the hook' ([math]::Abs($v - 0.8) -lt 0.001 -and $m -lt 0.5) ("target={0:N2}, master 0.50 -> {1:N2}" -f $v, $m)

    # 12b. Free hotkeys (any chord, RegisterHotKey): off in competitive mode, on when it is off, a taken chord is skipped
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    $tookF6 = [Kb]::RegisterHotKey([IntPtr]::Zero, 1, 0x4003, 0x75)   # this script owns Ctrl+Alt+F6 before the app starts
    @'
{
  "Bindings": [],
  "StartWithWindows": false,
  "CompetitiveMode": true,
  "CompetitiveHotkey": "Ctrl+Alt+F9",
  "FreeHotkeys": [
    { "Chord": "Ctrl+Alt+F8", "Action": "VolumeDown", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 },
    { "Chord": "Ctrl+Alt+F7", "Action": "Mute", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 5 },
    { "Chord": "Ctrl+Alt+F6", "Action": "VolumeDown", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 },
    { "Chord": "Ctrl+Alt+F8", "Action": "VolumeUp", "Target": { "Kind": "Foreground" }, "StepPercent": 5 },
    { "Chord": "F5", "Action": "Mute", "Target": { "Kind": "Foreground" }, "StepPercent": 5 }
  ],
  "PinnedApps": [ "powershell.exe" ],
  "HiddenApps": [ "notepad.exe" ]
}
'@ | Set-Content $settingsPath -Encoding UTF8
    $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
    function Chord([byte]$vk) { [Kb]::Down($VK_CONTROL); [Kb]::Down(0x12); [Kb]::Press($vk); [Kb]::Up(0x12); [Kb]::Up($VK_CONTROL); Start-Sleep -Milliseconds 500 }
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    (TargetSession $player.Id).SimpleAudioVolume.Mute = $false
    Chord 0x77
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Free hotkey: nothing happens in competitive mode' ((-not $app.HasExited) -and [math]::Abs($v - 0.8) -lt 0.001) ("target={0:N2} (expected 0.80)" -f $v)
    ToggleMode   # competitive off: the free hotkeys get registered
    Chord 0x77
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Free hotkey: Ctrl+Alt+F8 lowers the target by its step' ([math]::Abs($v - 0.7) -lt 0.011) ("target={0:N2} (expected 0.70)" -f $v)
    Chord 0x76
    $mu1 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    Chord 0x76
    $mu2 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    Check 'Free hotkey: Ctrl+Alt+F7 toggles the target mute' ($mu1 -and -not $mu2) "after 1st=$mu1, after 2nd=$mu2"
    Chord 0x75
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    $app.Refresh()
    Check 'Free hotkey: a chord owned by another program is skipped, no crash' ($tookF6 -and -not $app.HasExited -and [math]::Abs($v - 0.7) -lt 0.011) ("script registered F6=$tookF6, target={0:N2} (expected 0.70), app alive=$(-not $app.HasExited)" -f $v)
    Chord 0x77
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Free hotkey: a duplicate chord keeps the first entry only' ([math]::Abs($v - 0.6) -lt 0.011) ("target={0:N2} (expected 0.60)" -f $v)
    ToggleMode   # competitive on again: unregistered
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    Chord 0x77
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Free hotkey: unregistered again when competitive mode returns' ([math]::Abs($v - 0.8) -lt 0.001) ("target={0:N2} (expected 0.80)" -f $v)
    [void][Kb]::UnregisterHotKey([IntPtr]::Zero, 1)

    # 13. Automatic competitive mode: ping.exe on the list turns the mode on while it runs, and off when it exits
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    @'
{
  "Bindings": [
    { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 }
  ],
  "StartWithWindows": false,
  "AutoPrograms": [ { "ExeName": "ping.exe", "Enabled": true } ]
}
'@ | Set-Content $settingsPath -Encoding UTF8
    $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
    function ShiftVolDown {
        (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
        (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
        [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 900
        [pscustomobject]@{ Target = (TargetSession $player.Id).SimpleAudioVolume.Volume; Master = (Master).MasterVolumeLevelScalar }
    }
    $r = ShiftVolDown
    Check 'Auto mode: shortcuts work while no listed program runs' ([math]::Abs($r.Target - 0.7) -lt 0.011) ("target={0:N2} (expected 0.70)" -f $r.Target)
    $ping = Start-Process ping.exe -ArgumentList '-n', '60', '127.0.0.1' -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 7
    $r = ShiftVolDown
    Check 'Auto mode: listed program running turns competitive mode on' ([math]::Abs($r.Target - 0.8) -lt 0.001 -and $r.Master -lt 0.5) ("target={0:N2}, master 0.50 -> {1:N2}" -f $r.Target, $r.Master)
    Stop-Process -Id $ping.Id -Force
    Start-Sleep -Seconds 7
    $r = ShiftVolDown
    Check 'Auto mode: program exit turns it back off' ([math]::Abs($r.Target - 0.7) -lt 0.011) ("target={0:N2} (expected 0.70)" -f $r.Target)

    # 14. Remapped key: F13 acts as Vol-. Shift+F13 uses the Shift binding, a bare F13 is the real Vol- (master), no loop.
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    @'
{
  "Bindings": [
    { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 }
  ],
  "StartWithWindows": false,
  "KeyRemaps": [ { "Action": "VolumeDown", "Key": "F13" } ]
}
'@ | Set-Content $settingsPath -Encoding UTF8
    $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
    $VK_F13 = 0x7C
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_F13); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 600
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume; $m = (Master).MasterVolumeLevelScalar
    Check 'Remap: Shift+F13 lowers the bound app' ([math]::Abs($v - 0.7) -lt 0.011 -and [math]::Abs($m - 0.5) -lt 0.005) ("target={0:N2} (expected 0.70), master {1:N2}" -f $v, $m)
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Press($VK_F13); Start-Sleep -Milliseconds 900
    $m = (Master).MasterVolumeLevelScalar
    Check 'Remap: bare F13 lowers the master volume once' ($m -lt 0.5 -and $m -gt 0.44) ("master 0.50 -> {0:N2}" -f $m)
    $app.Refresh()
    Check 'Remap: app alive, no key loop' (-not $app.HasExited) "exited=$($app.HasExited)"

    # 14b. Command line, maximum volume, mute everything and profiles (all through the running app)
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    function SsCmd([string[]]$cmd) { (Start-Process $Exe -ArgumentList $cmd -Wait -PassThru -WindowStyle Hidden).ExitCode }
    Check 'Command line: exit code 2 when the app is not running' ((SsCmd @('--set', 'powershell.exe', '30')) -eq 2) 'app stopped'
    @'
{
  "Bindings": [
    { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 }
  ],
  "StartWithWindows": false,
  "MuteAllHotkey": "Ctrl+Alt+F4",
  "MaxVolumes": [ { "ExeName": "powershell.exe", "Percent": 40 } ],
  "Profiles": [ { "Name": "E2E profile", "Apps": [ { "ExeName": "powershell.exe", "Volume": 25 } ] } ]
}
'@ | Set-Content $settingsPath -Encoding UTF8
    $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
    (TargetSession $player.Id).SimpleAudioVolume.Mute = $false
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    Start-Sleep -Milliseconds 700   # the cap watch brings the target (0.80 > 40 %) back by itself
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Maximum volume: an app above its cap is brought back' ([math]::Abs($v - 0.4) -lt 0.011) ("target={0:N2} (expected 0.40)" -f $v)
    $rc = SsCmd @('--set', 'powershell', '30'); Start-Sleep -Milliseconds 300
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Command line: --set changes the app volume' ($rc -eq 0 -and [math]::Abs($v - 0.3) -lt 0.011) ("exit=$rc, target={0:N2} (expected 0.30)" -f $v)
    $rc = SsCmd @('--set', 'powershell.exe', '90'); Start-Sleep -Milliseconds 300
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Maximum volume: --set stops at the cap' ($rc -eq 0 -and [math]::Abs($v - 0.4) -lt 0.011) ("exit=$rc, target={0:N2} (expected 0.40)" -f $v)
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.35; Start-Sleep -Milliseconds 300
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_UP); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 500
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Maximum volume: a shortcut stops at the cap' ([math]::Abs($v - 0.4) -lt 0.011) ("target={0:N2} (expected 0.40, not 0.45)" -f $v)
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 1.0; Start-Sleep -Milliseconds 800
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Maximum volume: raising it from outside is undone' ([math]::Abs($v - 0.4) -lt 0.011) ("target={0:N2} (expected 0.40)" -f $v)
    $rc = SsCmd @('--mute', 'powershell.exe'); Start-Sleep -Milliseconds 300
    $mu1 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    $rc2 = SsCmd @('--unmute', 'powershell.exe'); Start-Sleep -Milliseconds 300
    $mu2 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    Check 'Command line: --mute and --unmute' ($rc -eq 0 -and $rc2 -eq 0 -and $mu1 -and -not $mu2) "exit $rc/$rc2, muted=$mu1 then $mu2"
    Check 'Command line: unknown app gives exit code 3' ((SsCmd @('--set', 'no-such-app.exe', '10')) -eq 3) 'not playing'
    Check 'Command line: bad arguments give exit code 1' (((SsCmd @('--set', 'powershell.exe', '150')) -eq 1) -and ((SsCmd @('--frobnicate')) -eq 1)) 'usage errors'
    $rc = SsCmd @('--profile', 'E2E profile'); Start-Sleep -Milliseconds 300
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Profile: --profile applies the volumes' ($rc -eq 0 -and [math]::Abs($v - 0.25) -lt 0.011) ("exit=$rc, target={0:N2} (expected 0.25)" -f $v)
    Check 'Profile: an unknown profile gives exit code 3' ((SsCmd @('--profile', 'nope')) -eq 3) 'no such profile'
    # Mute everything: the shortcut mutes the target, the second press restores it; the master is untouched.
    (TargetSession $player.Id).SimpleAudioVolume.Mute = $false
    $m0 = (Master).MasterVolumeLevelScalar; $mm0 = (Master).Mute
    Chord 0x73; Start-Sleep -Milliseconds 300
    $mu1 = (TargetSession $player.Id).SimpleAudioVolume.Mute; $mmaster1 = (Master).Mute
    Chord 0x73; Start-Sleep -Milliseconds 300
    $mu2 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    Check 'Mute everything: the shortcut mutes, the second press restores' ($mu1 -and -not $mu2 -and $mmaster1 -eq $mm0 -and [math]::Abs((Master).MasterVolumeLevelScalar - $m0) -lt 0.005) "muted=$mu1 then $mu2, master mute unchanged=$($mmaster1 -eq $mm0)"
    (TargetSession $player.Id).SimpleAudioVolume.Mute = $true
    $rc = SsCmd @('--mute-all'); Start-Sleep -Milliseconds 300
    $rc2 = SsCmd @('--mute-all'); Start-Sleep -Milliseconds 300
    $mu3 = (TargetSession $player.Id).SimpleAudioVolume.Mute
    Check 'Mute everything: --mute-all restores a previously muted app as it was' ($rc -eq 0 -and $rc2 -eq 0 -and $mu3) "exit $rc/$rc2, still muted=$mu3"
    (TargetSession $player.Id).SimpleAudioVolume.Mute = $false
    # Competitive mode through the command line: refused except its own toggle
    $rc = SsCmd @('--toggle-competitive'); Start-Sleep -Milliseconds 600
    $rcRefused = SsCmd @('--set', 'powershell.exe', '20'); Start-Sleep -Milliseconds 300
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume
    $rcBack = SsCmd @('--toggle-competitive'); Start-Sleep -Milliseconds 600
    $rcOk = SsCmd @('--set', 'powershell.exe', '20'); Start-Sleep -Milliseconds 300
    $v2 = (TargetSession $player.Id).SimpleAudioVolume.Volume
    Check 'Command line: refused (exit 4) in competitive mode, accepted again after the toggle' ($rc -eq 0 -and $rcRefused -eq 4 -and [math]::Abs($v - 0.25) -lt 0.011 -and $rcBack -eq 0 -and $rcOk -eq 0 -and [math]::Abs($v2 - 0.2) -lt 0.011) ("exit $rc/$rcRefused/$rcBack/$rcOk, target {0:N2} then {1:N2}" -f $v, $v2)

    # 15. Tray menu (WPF flyout): opens above the taskbar, closes on Escape and on an outside click, keyboard drives it.
    Stop-Process -Id $app.Id -Force; $app.WaitForExit(3000) | Out-Null
    @'
{
  "Bindings": [
    { "Modifiers": "Shift", "Target": { "Kind": "Process", "ExeName": "powershell.exe" }, "StepPercent": 10 }
  ],
  "StartWithWindows": false
}
'@ | Set-Content $settingsPath -Encoding UTF8
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win {
    public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, out RECT r, uint c);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public static void RightClickTray() { PostMessage(FindWindow(null, "SonicScrewdriverTray"), 0x8001, IntPtr.Zero, (IntPtr)0x205); }
    public static void Activate(string cls) { SetForegroundWindow(FindWindow(cls, null)); }
    public static RECT WorkArea() { RECT r; SystemParametersInfo(0x30, 0, out r, 0); return r; }
    // Rectangle of the visible top-level window with this title in this process, or Left=-1 when none is visible.
    public static RECT Visible(string title, uint pid) {
        RECT found = new RECT(); found.Left = -1;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (p == pid && sb.ToString() == title && IsWindowVisible(h)) { GetWindowRect(h, out found); return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
    $app = Start-Process $Exe -PassThru; Start-Sleep -Milliseconds 2500; $app.Refresh()
    $menuTitle = 'Sonic Screwdriver menu'
    # Put the cursor on the taskbar edge of the primary monitor, as a real click on the tray icon would.
    Add-Type -AssemblyName System.Windows.Forms
    $cursor0 = [Windows.Forms.Cursor]::Position
    $wa = [Win]::WorkArea(); $taskbarBottom = $wa.Bottom -lt [Win]::GetSystemMetrics(1)
    [void][Win]::SetCursorPos($wa.Right - 80, $(if ($taskbarBottom) { $wa.Bottom + 8 } else { $wa.Bottom - 8 }))
    [Win]::RightClickTray(); Start-Sleep -Milliseconds 800
    $r = [Win]::Visible($menuTitle, $app.Id)
    Check 'Tray menu: opens inside the work area, above the taskbar' ($r.Left -ge 0 -and $r.Bottom -le $wa.Bottom -and $r.Top -ge $wa.Top -and $r.Right -le $wa.Right) ("menu {0},{1}-{2},{3}; work area bottom {4}, taskbar at bottom: {5}" -f $r.Left, $r.Top, $r.Right, $r.Bottom, $wa.Bottom, $taskbarBottom)
    [Kb]::Press(0x1B); Start-Sleep -Milliseconds 500
    Check 'Tray menu: Escape closes it' ([Win]::Visible($menuTitle, $app.Id).Left -eq -1) 'hidden after Escape'
    Start-Sleep -Milliseconds 500
    [Win]::RightClickTray(); Start-Sleep -Milliseconds 800
    $opened = [Win]::Visible($menuTitle, $app.Id).Left -ge 0
    [Win]::Activate('Shell_TrayWnd'); Start-Sleep -Milliseconds 600
    Check 'Tray menu: a click elsewhere (focus lost) closes it' ($opened -and [Win]::Visible($menuTitle, $app.Id).Left -eq -1) "opened=$opened"
    Start-Sleep -Milliseconds 500
    [Win]::RightClickTray(); Start-Sleep -Milliseconds 800
    [Kb]::Press(0x28); [Kb]::Press(0x28); [Kb]::Press(0x0D); Start-Sleep -Milliseconds 900   # Down, Down (Competitive mode), Enter
    $menuClosed = [Win]::Visible($menuTitle, $app.Id).Left -eq -1
    (TargetSession $player.Id).SimpleAudioVolume.Volume = 0.8
    (Master).MasterVolumeLevelScalar = 0.5; Start-Sleep -Milliseconds 200
    [Kb]::Down($VK_SHIFT); [Kb]::Press($VK_VOL_DOWN); [Kb]::Up($VK_SHIFT); Start-Sleep -Milliseconds 900
    $v = (TargetSession $player.Id).SimpleAudioVolume.Volume; $m = (Master).MasterVolumeLevelScalar
    [Windows.Forms.Cursor]::Position = $cursor0
    Check 'Tray menu: keyboard (Down, Down, Enter) toggles competitive mode' ($menuClosed -and [math]::Abs($v - 0.8) -lt 0.001 -and $m -lt 0.5) ("menu closed=$menuClosed, target={0:N2}, master 0.50 -> {1:N2}" -f $v, $m)
}
finally {
    Get-Process ping -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-5) } | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    Get-Process SonicScrewdriver -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($player -and -not $player.HasExited) { Stop-Process -Id $player.Id -Force }
    (Master).MasterVolumeLevelScalar = $masterBefore
    (Master).Mute = $masterMuteBefore
    if ($hadSettings) { Move-Item "$settingsPath.e2e-backup" $settingsPath -Force } else { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
    Remove-Item $wav -ErrorAction SilentlyContinue
}
$results | Format-Table -AutoSize | Out-String -Width 200
"{0}/{1} passed" -f ($results | Where-Object Result -eq 'PASS').Count, $results.Count
