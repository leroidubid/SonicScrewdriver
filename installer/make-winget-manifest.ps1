# Generates the three winget manifest files for one release, with the real SHA256 of the installer.
# Used by the release workflow; can also be run locally:
#   pwsh installer/make-winget-manifest.ps1 -Version 1.0.0 -Setup publish/SonicScrewdriver-Setup-1.0.0.exe -OutDir out
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Setup,
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$Repo = 'leroidubid/SonicScrewdriver',
    [string]$Publisher = 'Remi Chanoine'
)
$ErrorActionPreference = 'Stop'

$owner = $Repo.Split('/')[0]
$id = [cultureinfo]::InvariantCulture.TextInfo.ToTitleCase($owner) + '.SonicScrewdriver'
$sha = (Get-FileHash $Setup -Algorithm SHA256).Hash
$file = Split-Path $Setup -Leaf
$base = "https://github.com/$Repo"
$schema = 'https://aka.ms/winget-manifest'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Write-Manifest([string]$name, [string]$text) {
    Set-Content -Path (Join-Path $OutDir $name) -Value $text.TrimStart() -Encoding utf8NoBOM
}

Write-Manifest "$id.yaml" @"
# yaml-language-server: `$schema=$schema.version.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

Write-Manifest "$id.installer.yaml" @"
# yaml-language-server: `$schema=$schema.installer.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: user
MinimumOSVersion: 10.0.17763.0
InstallModes:
  - interactive
  - silent
  - silentWithProgress
UpgradeBehavior: install
Dependencies:
  PackageDependencies:
    - PackageIdentifier: Microsoft.DotNet.DesktopRuntime.8
Installers:
  - Architecture: x64
    InstallerUrl: $base/releases/download/v$Version/$file
    InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.6.0
"@

Write-Manifest "$id.locale.en-US.yaml" @"
# yaml-language-server: `$schema=$schema.defaultLocale.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: $Publisher
PublisherUrl: https://github.com/$owner
PackageName: Sonic Screwdriver
PackageUrl: $base
License: MIT with Commons Clause
LicenseUrl: $base/blob/main/LICENSE
ShortDescription: Per-app volume mixer for the Windows tray, with modifier + media key shortcuts.
Description: >-
  A lightweight tray mixer that shows every output device and the apps playing on it, and lets you bind
  modifier + media key shortcuts to one specific app, the foreground app, or the output devices
  (Vol+ / Vol- cycle the default output).
Moniker: sonicscrewdriver
Tags:
  - audio
  - volume
  - mixer
  - media-keys
  - tray
ReleaseNotesUrl: $base/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

Write-Host "Wrote manifests for $id $Version (SHA256 $sha) to $OutDir"
