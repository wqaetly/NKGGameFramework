param(
    [string]$Configuration = "Release",
    [string]$GodotExe = "C:\study\godot\GodotEngine\Godot_v4.7-stable_win64_console.exe",
    [switch]$HeadlessCheck,
    [switch]$SkipNativeBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$managedProject = Join-Path $repoRoot "samples\NKGGameFramework.GodotPlaneSample\NKGGameFramework.GodotPlaneSample.csproj"
$godotProject = Join-Path $repoRoot "samples\GodotPlaneSample"
$godotCacheDir = Join-Path $godotProject ".godot"
$nativeBuildScript = Join-Path $godotProject "native\build-gdextension.ps1"
$stageBclScript = Join-Path $godotProject "tools\stage-leanclr-bcl.ps1"

dotnet build $managedProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not $SkipNativeBuild) {
    powershell -ExecutionPolicy Bypass -File $nativeBuildScript -Configuration $Configuration -GodotExe $GodotExe
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

powershell -ExecutionPolicy Bypass -File $stageBclScript
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force $godotCacheDir | Out-Null
Set-Content -Encoding UTF8 `
    -Path (Join-Path $godotCacheDir "extension_list.cfg") `
    -Value "res://addons/nkg_leanclr_bridge/nkg_leanclr_bridge.gdextension"

if ($HeadlessCheck) {
    & $GodotExe --headless --path $godotProject --script "res://tools/main_scene_smoke.gd"
}
else {
    & $GodotExe --path $godotProject
}
exit $LASTEXITCODE
