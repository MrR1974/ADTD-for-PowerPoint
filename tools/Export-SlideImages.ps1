<#
.SYNOPSIS
  Renders every slide of each .pptx to PNG using PowerPoint.
  Developer tooling for checking output by eye.

  Output: <OutDir>\<file>\slide<N>.png
#>
param(
    [Parameter(Mandatory)] [string[]] $Path,
    [Parameter(Mandatory)] [string]   $OutDir,
    [int] $Dpi = 96
)

$ErrorActionPreference = 'Stop'
# PowerPoint resolves relative paths against its own working folder, not ours.
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$ppt = New-Object -ComObject PowerPoint.Application
try {
    foreach ($file in Get-ChildItem $Path -Include *.pptx) {
        $target = Join-Path $OutDir $file.BaseName
        New-Item -ItemType Directory -Force $target | Out-Null
        # ReadOnly, Untitled = false, WithWindow = false
        $pres = $ppt.Presentations.Open($file.FullName, -1, 0, 0)
        try {
            $w = [int][math]::Round($pres.PageSetup.SlideWidth / 72 * $Dpi)
            $h = [int][math]::Round($pres.PageSetup.SlideHeight / 72 * $Dpi)
            foreach ($slide in $pres.Slides) {
                $slide.Export((Join-Path $target "slide$($slide.SlideIndex).png"), 'PNG', $w, $h)
            }
            Write-Host "$($file.Name): $($pres.Slides.Count) slide(s) at $w x $h"
        }
        finally { $pres.Close() }
    }
}
finally { $ppt.Quit() }
