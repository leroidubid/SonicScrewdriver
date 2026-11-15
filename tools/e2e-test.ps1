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
  "CompetitiveMode": true
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
}
finally {
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
