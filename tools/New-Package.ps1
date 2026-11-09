<#
.SYNOPSIS
    Builds the release zip: the desktop app and the adtd command-line tool, self-contained (no .NET install
    needed), with the installer scripts.

.DESCRIPTION
    Produces

        dist\ADTD-for-PowerPoint-<version>.zip          app\ ("ADTD for PowerPoint.exe", adtd.exe and the .NET
                                                         runtime), Install-Adtd.ps1, Uninstall-Adtd.ps1,
                                                         "Install ADTD for PowerPoint.cmd", README.txt, LICENSE, NOTICE.md
        dist\ADTD-for-PowerPoint-<version>.zip.sha256   "<hash>  <file>", sha256sum style

    A zip rather than an MSI: an unsigned MSI is warned about by SmartScreen on every release, and nothing here
    needs administrator rights. Both programs are published into one folder so they share one copy of the
    runtime. Native .pdb files (libSkiaSharp.pdb alone is 88 MB) are left out.

    No template goes in the zip: the build stops if the staged files include a PowerPoint template or deck
    (.potx, .pptx, .thmx and the like) or any file from samples\local (corporate templates, real snapshots).

    For collecting on a machine without installing anything, tools\Publish-Collector.ps1 still builds adtd.exe
    as one self-contained file.

.PARAMETER Version   e.g. 1.0.0; stamped into both programs and shown in Settings > Apps.
.EXAMPLE             .\New-Package.ps1 -Version 1.0.0
#>
# Adapted from Recast's tools/New-Package.ps1.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$name = "ADTD-for-PowerPoint-$Version"
$stage = Join-Path $dist $name
$app = Join-Path $stage 'app'
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $app | Out-Null

# The projects set RuntimeIdentifier themselves (passing -r as well breaks restore).
foreach ($project in 'src\Adtd.App', 'src\Adtd.Cli') {
    Write-Host "Publishing $project"
    dotnet publish (Join-Path $root $project) -c Release --self-contained -p:Version=$Version -p:IncludeSourceRevisionInInformationalVersion=false `
        -p:DebugType=none -p:GenerateDocumentationFile=false -o $app -v q -nologo
    if ($LASTEXITCODE) { throw "dotnet publish $project failed" }
}
Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item -Force
foreach ($exe in 'ADTD for PowerPoint.exe', 'adtd.exe') {
    if (-not (Test-Path (Join-Path $app $exe))) { throw "Publish produced no $exe" }
}

Copy-Item (Join-Path $PSScriptRoot 'Install-Adtd.ps1'), (Join-Path $PSScriptRoot 'Uninstall-Adtd.ps1'),
          (Join-Path $PSScriptRoot 'Install ADTD for PowerPoint.cmd'), (Join-Path $root 'LICENSE'), (Join-Path $root 'NOTICE.md') $stage
@"
ADTD for PowerPoint ${Version}: draws Active Directory domains, trusts, sites and domain controllers as native,
editable PowerPoint slides in your own template.

To install, double-click "Install ADTD for PowerPoint.cmd". It installs for you only (no administrator rights)
into %LOCALAPPDATA%\Programs\ADTD for PowerPoint, adds it to the Start menu, and lists it in Settings > Apps,
where it can be uninstalled. Add -AddToPath (run Install-Adtd.ps1 from PowerShell) to use the adtd command in
any terminal.

PowerPoint isn't needed to make the slides, only to open them. Collecting needs a machine in the domain (or one
that can reach a domain controller); otherwise collect there with 'adtd collect' and open the snapshot here.

No PowerPoint template is included: use your own.

ADTD for PowerPoint is published under the MIT License (see LICENSE). It is a rebuild of ADTD.Net by Goran
Mihelcic (MIT); see NOTICE.md.
"@ | Set-Content -Encoding UTF8 (Join-Path $stage 'README.txt')

# Nothing local or corporate leaves the machine: no templates or decks, nothing that came from samples\local.
$localNames = @(Get-ChildItem (Join-Path $root 'samples\local') -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object Name)
$leaks = @(Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Extension -in '.potx', '.potm', '.pot', '.pptx', '.pptm', '.ppt', '.thmx' -or $_.Name -in $localNames
})
if ($leaks) { throw "The release would include templates or local files: $($leaks.FullName -join ', ')" }

$zip = Join-Path $dist "$name.zip"
Remove-Item -Force $zip, "$zip.sha256" -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -Encoding ASCII -NoNewline "$zip.sha256"
Remove-Item -Recurse -Force $stage

Write-Host ("Wrote {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host "      $zip.sha256"
