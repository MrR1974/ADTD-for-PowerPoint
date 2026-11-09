<#
.SYNOPSIS
    Installs ADTD for PowerPoint for the current user, from an extracted release zip. No administrator rights needed.

.DESCRIPTION
    - Copies the app (and the adtd command-line tool) to the install folder, replacing an earlier version.
    - Adds a Start menu shortcut.
    - Lists the app in Settings > Apps, where it can be uninstalled.
    Everything is per-user: files under %LOCALAPPDATA%, settings under HKEY_CURRENT_USER. The app's own settings
    (%LOCALAPPDATA%\ADTD-for-PowerPoint) are left as they are, so an upgrade keeps them.

.PARAMETER InstallPath  Where to install. Default: %LOCALAPPDATA%\Programs\ADTD for PowerPoint.
.PARAMETER AddToPath    Also put the install folder on your PATH, so `adtd` works in any terminal.
.PARAMETER Version      Shown in Settings > Apps. Default: read from the app.
#>
# Adapted from Recast's tools/Install-Recast.ps1 (no file associations: snapshots are plain .json).
[CmdletBinding()]
param(
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'Programs\ADTD for PowerPoint'),
    [switch] $AddToPath,
    [string] $Version
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string] $Message, [string] $Remedy) {
    Write-Host ''
    Write-Host $Message -ForegroundColor Red
    if ($Remedy) { Write-Host $Remedy -ForegroundColor Yellow }
    Write-Host ''
    exit 1
}

$appName = 'ADTD for PowerPoint'
$source = Join-Path $PSScriptRoot 'app'
$exeName = "$appName.exe"
if (-not (Test-Path (Join-Path $source $exeName))) {
    Fail "No app folder next to this script ($source)." 'Run this from the extracted release zip.'
}
if (-not $Version) { $Version = (Get-Item (Join-Path $source $exeName)).VersionInfo.ProductVersion -replace '\+.*$', '' }

Write-Host ''
Write-Host "$appName $Version" -ForegroundColor Cyan
Write-Host ''

# --- files -----------------------------------------------------------------------------------------
$exe = Join-Path $InstallPath $exeName
$running = Get-Process -Name $appName, 'adtd' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallPath, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { Fail "$appName is running from the install folder." 'Close it and run the installer again.' }
try {
    if (Test-Path $InstallPath) { Get-ChildItem -LiteralPath $InstallPath -Force | Remove-Item -Recurse -Force }
    else { New-Item -ItemType Directory -Force $InstallPath | Out-Null }
    Copy-Item -Path (Join-Path $source '*') -Destination $InstallPath -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-Adtd.ps1') -Destination $InstallPath -Force
    foreach ($doc in 'LICENSE', 'NOTICE.md') {
        if (Test-Path (Join-Path $PSScriptRoot $doc)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $doc) -Destination $InstallPath -Force }
    }
}
catch { Fail "Could not copy the files to ${InstallPath}: $($_.Exception.Message)" 'Nothing else was changed. Close anything using that folder and try again.' }
Write-Host "  installed to                  $InstallPath"

# --- Start menu ------------------------------------------------------------------------------------
$programs = [Environment]::GetFolderPath('Programs')
$lnk = Join-Path $programs "$appName.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $InstallPath
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Draw Active Directory topology as PowerPoint slides'
$shortcut.Save()
Write-Host "  Start menu                    $appName"

# --- PATH (optional) -------------------------------------------------------------------------------
if ($AddToPath) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { $_ })
    if ($parts -notcontains $InstallPath) {
        [Environment]::SetEnvironmentVariable('Path', (($parts + $InstallPath) -join ';'), 'User')
    }
    Write-Host "  PATH                          adtd (open a new terminal to use it)"
}

# --- Settings > Apps -------------------------------------------------------------------------------
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ADTD-for-PowerPoint'
New-Item -Path $uninstallKey -Force | Out-Null
$bytes = (Get-ChildItem -LiteralPath $InstallPath -File -Recurse | Measure-Object -Property Length -Sum).Sum
Set-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value $appName
Set-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $Version
Set-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value $appName
Set-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $InstallPath
Set-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value "$exe,0"
Set-ItemProperty -Path $uninstallKey -Name 'EstimatedSize' -Value ([int]($bytes / 1KB)) -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord
# -FromArp: launched by Settings, so the uninstaller keeps its window open to show what it did.
Set-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -FromArp' -f (Join-Path $InstallPath 'Uninstall-Adtd.ps1'))
Write-Host "  listed in                     Settings > Apps > Installed apps"

Write-Host ''
Write-Host "Done. Start $appName from the Start menu." -ForegroundColor Green
Write-Host ''
