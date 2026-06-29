param(
    [string]$BclTargetDir = (Join-Path (Join-Path $PSScriptRoot "..") "leanclr_bcl\net10.0"),
    [string]$RuntimeVersionPrefix = "10."
)

$ErrorActionPreference = "Stop"

$runtimeLine = dotnet --list-runtimes |
    Where-Object { $_ -like "Microsoft.NETCore.App $RuntimeVersionPrefix*" } |
    Select-Object -Last 1

if (-not $runtimeLine) {
    throw "Microsoft.NETCore.App $RuntimeVersionPrefix runtime assemblies were not found. Install .NET $RuntimeVersionPrefix SDK/runtime or provide a staged BCL directory."
}

$runtimeVersion = ($runtimeLine -split " ")[1]
$runtimeBase = $runtimeLine -replace "^.*\[", "" -replace "\].*$", ""
$runtimeDir = Join-Path $runtimeBase $runtimeVersion

if (-not (Test-Path $runtimeDir)) {
    throw "Runtime assembly directory does not exist: $runtimeDir"
}

New-Item -ItemType Directory -Force $BclTargetDir | Out-Null
Copy-Item -Force -Path (Join-Path $runtimeDir "System*.dll") -Destination $BclTargetDir

$files = Get-ChildItem -Path $BclTargetDir -Filter "System*.dll"
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
$totalMb = [math]::Round($totalBytes / 1MB, 2)

Write-Host "Staged LeanCLR BCL assemblies from $runtimeDir"
Write-Host "Target: $BclTargetDir"
Write-Host "Files: $($files.Count), Size: $totalMb MB"
