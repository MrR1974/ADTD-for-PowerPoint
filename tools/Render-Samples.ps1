<#
.SYNOPSIS
  Renders every sample forest on every template with 'adtd render', then every slide to PNG through PowerPoint, for
  review by eye. Developer tooling.

  Forests:   every built-in sample (single, contoso, hub, satellites, regional, spokes, large), and any samples\local\*.json (real snapshots, never committed).
  Templates: out\test-templates\* (built by tools\New-TestTemplates.ps1) and samples\local\*.potx / *.pptx.
  Output:    out\render\<template>\<forest>.pptx, and out\render\png\<forest>\slide<N>.png under each template folder.
  Exits 1 if any render reported layout problems or validation errors.
#>
param(
    [string[]] $Forests,
    [string[]] $Templates,
    [string]   $OutDir = (Join-Path $PSScriptRoot '..\out\render'),
    [switch]   $NoImages
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
dotnet build (Join-Path $root 'src\Adtd.Cli') -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$adtd = Join-Path $root 'src\Adtd.Cli\bin\Debug\net8.0-windows\win-x64\adtd.exe'

if (-not $Forests) {
    $Forests = @('single', 'contoso', 'hub', 'satellites', 'regional', 'spokes', 'large') + @(Get-ChildItem (Join-Path $root 'samples\local') -Filter *.json -ErrorAction SilentlyContinue | ForEach-Object FullName)
}
if (-not $Templates) {
    $Templates = @(Get-ChildItem (Join-Path $root 'out\test-templates\*'), (Join-Path $root 'samples\local\*') -Include *.potx, *.pptx -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
}
if (-not $Templates) { throw 'No templates found. Run tools\New-TestTemplates.ps1, or pass -Templates.' }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$failed = @()
foreach ($t in $Templates) {
    $tname = [IO.Path]::GetFileNameWithoutExtension($t) -replace '[^\w.-]+', '-'
    $dir = Join-Path $OutDir $tname
    New-Item -ItemType Directory -Force $dir | Out-Null
    $decks = @()
    foreach ($f in $Forests) {
        $fname = [IO.Path]::GetFileNameWithoutExtension($f)
        $out = Join-Path $dir "$fname.pptx"
        Write-Host "== $tname / $fname" -ForegroundColor Cyan
        & $adtd render $f --template $t --out $out
        if ($LASTEXITCODE -ne 0) { $failed += "$tname / $fname" }
        $decks += $out
    }
    if (-not $NoImages) { & (Join-Path $PSScriptRoot 'Export-SlideImages.ps1') -Path $decks -OutDir (Join-Path $dir 'png') }
}
if ($failed) {
    Write-Host "Problems in: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All renders clean. Output in $(Resolve-Path $OutDir)" -ForegroundColor Green
