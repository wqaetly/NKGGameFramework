param(
    [string]$ManagedAssemblyDir = (Join-Path (Join-Path (Join-Path $PSScriptRoot "..\..") "NKGGameFramework.GodotPlaneSample") "bin\Release\net10.0"),
    [string]$BclTargetDir = (Join-Path (Join-Path $PSScriptRoot "..") "leanclr_bcl\net10.0"),
    [string]$RuntimeVersionPrefix = "10."
)

$ErrorActionPreference = "Stop"

function Get-AssemblyReferences {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.Reflection.Assembly]::LoadFile($Path).GetReferencedAssemblies() |
        ForEach-Object { $_.Name }
}

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

if (-not (Test-Path $ManagedAssemblyDir)) {
    throw "Managed assembly directory does not exist: $ManagedAssemblyDir"
}

$ManagedAssemblyDir = (Resolve-Path $ManagedAssemblyDir).Path

$runtimeAssemblies = @{}
Get-ChildItem -Path $runtimeDir -Filter "*.dll" | ForEach-Object {
    $runtimeAssemblies[$_.BaseName] = $_.FullName
}

$required = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$queue = [System.Collections.Generic.Queue[string]]::new()

function Add-RuntimeAssembly {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not $runtimeAssemblies.ContainsKey($Name)) {
        return
    }

    if ($required.Add($Name)) {
        $queue.Enqueue($Name)
    }
}

# LeanCLR initializes CoreLib directly; it may not appear as a regular AssemblyRef.
Add-RuntimeAssembly "System.Private.CoreLib"

$managedFiles = Get-ChildItem -Path $ManagedAssemblyDir -Filter "*.dll"
foreach ($file in $managedFiles) {
    foreach ($reference in Get-AssemblyReferences $file.FullName) {
        Add-RuntimeAssembly $reference
    }
}

while ($queue.Count -gt 0) {
    $name = $queue.Dequeue()
    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($name, "System.Private.CoreLib")) {
        continue
    }

    foreach ($reference in Get-AssemblyReferences $runtimeAssemblies[$name]) {
        Add-RuntimeAssembly $reference
    }
}

New-Item -ItemType Directory -Force $BclTargetDir | Out-Null
Get-ChildItem -Path $BclTargetDir -Filter "*.dll" -ErrorAction SilentlyContinue | Remove-Item -Force

foreach ($name in ($required | Sort-Object)) {
    Copy-Item -Force -Path $runtimeAssemblies[$name] -Destination $BclTargetDir
}

$files = Get-ChildItem -Path (Join-Path $BclTargetDir "*.dll")
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
$totalMb = [math]::Round($totalBytes / 1MB, 2)

Write-Host "Staged LeanCLR BCL assemblies from $runtimeDir"
Write-Host "Managed: $ManagedAssemblyDir"
Write-Host "Target: $BclTargetDir"
Write-Host "Files: $($files.Count), Size: $totalMb MB"
