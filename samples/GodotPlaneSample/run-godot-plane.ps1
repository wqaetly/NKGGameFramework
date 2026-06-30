param(
    [string]$Configuration = "Release",
    [string]$GodotExe,
    [string]$GodotCppRoot,
    [string]$LeanClrRoot,
    [switch]$HeadlessCheck,
    [switch]$SkipNativeBuild,
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"

$runner = Join-Path $PSScriptRoot "tools\run-godot-plane.ps1"

$arguments = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $runner,
    "-Configuration", $Configuration
)

if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
    $arguments += @("-GodotExe", $GodotExe)
}
if (-not [string]::IsNullOrWhiteSpace($GodotCppRoot)) {
    $arguments += @("-GodotCppRoot", $GodotCppRoot)
}
if (-not [string]::IsNullOrWhiteSpace($LeanClrRoot)) {
    $arguments += @("-LeanClrRoot", $LeanClrRoot)
}
if ($HeadlessCheck) {
    $arguments += "-HeadlessCheck"
}
if ($SkipNativeBuild) {
    $arguments += "-SkipNativeBuild"
}
if (-not $NoClean) {
    $arguments += "-Clean"
}

& powershell @arguments
exit $LASTEXITCODE
