<#
.SYNOPSIS
    Builds a portable, self-contained WinHoldMenu into .\artifacts\<rid>\.
.EXAMPLE
    .\build.ps1                 # win-x64, Release
    .\build.ps1 -Runtime win-arm64
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$project = Join-Path $root 'src\WinHoldMenu\WinHoldMenu.csproj'
$output  = Join-Path $root "artifacts\$Runtime"
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

dotnet publish $project -c $Configuration -r $Runtime -p:Platform=$platform -o $output
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)" }

Write-Host "`nDone: $output\WinHoldMenu.exe" -ForegroundColor Green
