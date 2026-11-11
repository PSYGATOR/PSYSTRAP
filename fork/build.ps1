<#
.SYNOPSIS
  Builds the distributable single-file PSYstrap.exe into fork\dist\.

  Use this, not "dotnet build": a plain build makes a tiny launcher exe plus PSYstrap.dll,
  and the installer only copies the exe into %LocalAppData%, so the installed copy can't start.
  Same publish settings as upstream's .github/workflows/ci-release.yml.
  Needs the .NET 6 Desktop Runtime on the PC that runs it (same as upstream Bloxstrap).
#>
param([ValidateSet('Release','Debug')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $PSScriptRoot 'dist'

$commit = (git -C $root rev-parse HEAD 2>$null)
$branch = (git -C $root rev-parse --abbrev-ref HEAD 2>$null)

dotnet publish (Join-Path $root 'Bloxstrap\Bloxstrap.csproj') `
    -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:CommitHash=$commit -p:CommitRef=branch/$branch `
    -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# single-file publish still drops .pdb/.xml files next to the exe; they aren't needed to run it
Get-ChildItem $dist -Include *.pdb, *.xml -Recurse | Remove-Item

# Bloxstrap's MIT license has to travel with the app
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'License - Bloxstrap.txt') -Force

Write-Host ""
Get-ChildItem $dist -Filter *.exe | ForEach-Object { Write-Host "Built $($_.FullName) ($([Math]::Round($_.Length/1MB,1)) MB)" }
