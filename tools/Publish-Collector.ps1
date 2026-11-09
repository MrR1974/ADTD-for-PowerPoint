<#
.SYNOPSIS
  Builds adtd.exe as one self-contained file, to copy onto a domain-joined machine and run there:
    adtd collect --out forest.json
  No .NET install or admin rights are needed on that machine. Bring back forest.json and forest.log.

  Output: dist\adtd-<version>-win-x64.exe (+ .sha256)
#>
param([string] $Version = '0.1.0')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $root 'out\publish-cli'
$dist = Join-Path $root 'dist'
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

dotnet publish (Join-Path $root 'src\Adtd.Cli\Adtd.Cli.csproj') -c Release --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -p:Version=$Version -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $dist "adtd-$Version-win-x64.exe"
Copy-Item (Join-Path $staging 'adtd.exe') $exe -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $exe -Leaf)" | Set-Content "$exe.sha256" -Encoding ascii
'{0}  {1:N1} MB  sha256 {2}' -f $exe, ((Get-Item $exe).Length / 1MB), $hash
