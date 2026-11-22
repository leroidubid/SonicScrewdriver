# Generates the Chocolatey package (nuspec + install and uninstall scripts) for one release.
# Used by .github/workflows/chocolatey.yml; can also be run locally:
#   pwsh installer/make-choco-package.ps1 -Version 1.2.0 -Sha256 <sha256 of SonicScrewdriver-Setup-1.2.0.exe> -OutDir out
#   choco pack out/sonicscrewdriver.nuspec
# The package does not embed the installer: the install script downloads it from the GitHub release and checks its SHA256.
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Sha256,
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$Repo = 'leroidubid/SonicScrewdriver',
    [string]$Authors = 'Remi Chanoine'
)
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not a plain x.y.z release version (pre-releases are not pushed)." }
if ($Sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Sha256 '$Sha256' is not a SHA256 hash." }
$sha = $Sha256.ToLower()

$owner = $Repo.Split('/')[0]
$base = "https://github.com/$Repo"
$file = "SonicScrewdriver-Setup-$Version.exe"
New-Item -ItemType Directory -Force (Join-Path $OutDir 'tools') | Out-Null

function Write-Utf8([string]$path, [string]$text) {
    Set-Content -Path (Join-Path $OutDir $path) -Value $text.TrimStart() -Encoding utf8NoBOM
}

Write-Utf8 'sonicscrewdriver.nuspec' @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>sonicscrewdriver</id>
    <version>$Version</version>
    <title>Sonic Screwdriver</title>
    <authors>$Authors</authors>
    <owners>$owner</owners>
    <projectUrl>$base</projectUrl>
    <packageSourceUrl>$base/tree/v$Version/installer</packageSourceUrl>
    <projectSourceUrl>$base</projectSourceUrl>
    <docsUrl>$base/wiki</docsUrl>
    <bugTrackerUrl>$base/issues</bugTrackerUrl>
    <iconUrl>https://raw.githubusercontent.com/$Repo/v$Version/site/img/icon-256.png</iconUrl>
    <licenseUrl>$base/blob/v$Version/LICENSE</licenseUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <tags>sonicscrewdriver audio volume mixer media-keys tray windows</tags>
    <summary>Per-app volume mixer for the Windows tray, with modifier + media key shortcuts.</summary>
    <description><![CDATA[
A lightweight tray mixer that shows every output device and the apps playing on it, and lets you bind modifier + media key
shortcuts to one specific app, the foreground app or the output devices.

* Per-app volume and mute, set from the mixer, from shortcuts on the media keys, or from the command line.
* Competitive mode: one switch turns off the keyboard hook, the overlay and every observer, for online games with an anti-cheat.
* Volume profiles, automatic ducking, a streamer mode driven by the microphone, a microphone shortcut, a maximum volume per app,
  remembered output routing and an output device priority list.
* About 20 MB of RAM and no CPU at idle; no network use except the update check you start, nothing collected.

The license is MIT with the Commons Clause (source-available): free to use, modify and share, not to sell.

The package downloads the installer of the release from GitHub and checks its SHA256. The installer is per-user: it needs no
administrator rights of its own, so under an elevated `choco install` it installs for the account that runs it.
Windows may show a SmartScreen warning because the files are not code-signed.
]]></description>
    <releaseNotes>$base/releases/tag/v$Version</releaseNotes>
    <dependencies>
      <dependency id="dotnet-8.0-desktopruntime" version="8.0.0" />
    </dependencies>
  </metadata>
  <files>
    <file src="tools\**" target="tools" />
  </files>
</package>
"@

Write-Utf8 'tools\chocolateyInstall.ps1' @"
`$ErrorActionPreference = 'Stop'

`$packageArgs = @{
  packageName    = 'sonicscrewdriver'
  fileType       = 'exe'
  url64bit       = '$base/releases/download/v$Version/$file'
  checksum64     = '$sha'
  checksumType64 = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
  softwareName   = 'Sonic Screwdriver*'
}

Install-ChocolateyPackage @packageArgs
"@

Write-Utf8 'tools\chocolateyUninstall.ps1' @"
`$ErrorActionPreference = 'Stop'

`$packageArgs = @{
  packageName    = 'sonicscrewdriver'
  fileType       = 'exe'
  softwareName   = 'Sonic Screwdriver*'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
  validExitCodes = @(0)
}

# The installer is per-user, so its uninstall entry is under HKCU of the account that installed it.
`$keys = @(Get-UninstallRegistryKey -SoftwareName `$packageArgs.softwareName)
if (`$keys.Count -eq 1) {
  `$packageArgs['file'] = `$keys[0].UninstallString.Trim('"')
  Uninstall-ChocolateyPackage @packageArgs
}
elseif (`$keys.Count -eq 0) {
  Write-Warning 'Sonic Screwdriver was not found for this account (installed for another user?). Nothing was uninstalled.'
}
else {
  Write-Warning "`$(`$keys.Count) matches for 'Sonic Screwdriver*': not uninstalling anything. Use Settings, Apps."
}
"@

Write-Host "Wrote the Chocolatey package for sonicscrewdriver $Version (SHA256 $sha) to $OutDir"
