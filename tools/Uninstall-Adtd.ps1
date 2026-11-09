<#
.SYNOPSIS
    Removes ADTD for PowerPoint for the current user: the install folder, the Start menu shortcut, the PATH entry
    (if added) and the Settings > Apps listing. Only what Install-Adtd.ps1 added.

.DESCRIPTION
    The app's settings and collection logs (%LOCALAPPDATA%\ADTD-for-PowerPoint) are kept unless -RemoveSettings is
    given, so a reinstall picks up where you left off. Snapshots and decks you saved elsewhere are never touched.

.PARAMETER InstallPath     Default: the folder this script is in when it's in an install, else the default install folder.
.PARAMETER RemoveSettings  Also delete the app's settings and collection logs.
.PARAMETER FromArp         Set when Settings > Apps runs it: keeps the window open at the end.
#>
# Adapted from Recast's tools/Uninstall-Recast.ps1.
[CmdletBinding()]
param(
    [string] $InstallPath,
    [switch] $RemoveSettings,
    [switch] $FromArp
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'ADTD for PowerPoint'
if (-not $InstallPath) {
    $InstallPath = if (Test-Path (Join-Path $PSScriptRoot "$appName.exe")) { $PSScriptRoot } else { Join-Path $env:LOCALAPPDATA "Programs\$appName" }
}
$exe = Join-Path $InstallPath "$appName.exe"

Write-Host ''
Write-Host "Removing $appName" -ForegroundColor Cyan
Write-Host ''
try {
    $running = Get-Process -Name $appName, 'adtd' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallPath, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "$appName is running. Close it and run the uninstaller again." }

    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
    if (Test-Path $lnk) { Remove-Item -Force $lnk; Write-Host '  removed                       Start menu shortcut' }

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath) {
        $parts = @($userPath -split ';' | Where-Object { $_ })
        if ($parts -contains $InstallPath) {
            [Environment]::SetEnvironmentVariable('Path', (($parts | Where-Object { $_ -ne $InstallPath }) -join ';'), 'User')
            Write-Host '  removed                       PATH entry'
        }
    }

    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ADTD-for-PowerPoint'
    if (Test-Path $uninstallKey) { Remove-Item -Recurse -Force $uninstallKey; Write-Host '  removed                       Settings > Apps listing' }

    # Only delete a folder that is recognisably an install of this app.
    if (Test-Path $exe) {
        Set-Location $env:TEMP   # this script may be running from inside the folder
        Remove-Item -Recurse -Force $InstallPath
        Write-Host "  removed                       $InstallPath"
    }
    elseif (Test-Path $InstallPath) { Write-Host "  left alone                    $InstallPath (no $appName.exe in it)" -ForegroundColor Yellow }

    $data = Join-Path $env:LOCALAPPDATA 'ADTD-for-PowerPoint'
    if (Test-Path $data) {
        if ($RemoveSettings) { Remove-Item -Recurse -Force $data; Write-Host "  removed                       $data (settings and logs)" }
        else { Write-Host "  kept                          $data (settings and logs; -RemoveSettings deletes them)" }
    }

    Write-Host ''
    Write-Host "$appName has been removed." -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
}
Write-Host ''
if ($FromArp) { Read-Host 'Press Enter to close' | Out-Null }
