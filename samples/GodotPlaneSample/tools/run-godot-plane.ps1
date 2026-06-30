param(
    [string]$Configuration = "Release",
    [string]$GodotExe,
    [string]$GodotCppRoot,
    [string]$LeanClrRoot,
    [switch]$HeadlessCheck,
    [switch]$SkipNativeBuild,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$managedProject = Join-Path $repoRoot "samples\NKGGameFramework.GodotPlaneSample\NKGGameFramework.GodotPlaneSample.csproj"
$godotProject = Join-Path $repoRoot "samples\GodotPlaneSample"
$godotCacheDir = Join-Path $godotProject ".godot"
$nativeBuildScript = Join-Path $godotProject "native\build-gdextension.ps1"
$stageBclScript = Join-Path $godotProject "tools\stage-leanclr-bcl.ps1"
$ensureScript = Join-Path $godotProject "tools\ensure-godot-4.7.ps1"

$ensureArgs = @("-ExecutionPolicy", "Bypass", "-File", $ensureScript)
if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
    $ensureArgs += @("-GodotExe", $GodotExe)
}
if (-not [string]::IsNullOrWhiteSpace($GodotCppRoot)) {
    $ensureArgs += @("-GodotCppRoot", $GodotCppRoot)
}
if (-not [string]::IsNullOrWhiteSpace($LeanClrRoot)) {
    $ensureArgs += @("-LeanClrRoot", $LeanClrRoot)
}
if ($SkipNativeBuild) {
    $ensureArgs += "-SkipGodotCpp"
}

$toolInfo = (& powershell @ensureArgs | Select-Object -Last 1) | ConvertFrom-Json
$GodotExe = $toolInfo.GodotExe
$GodotCppRoot = $toolInfo.GodotCppRoot
$LeanClrRoot = $toolInfo.LeanClrRoot
if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    throw "Godot executable could not be resolved. Pass -GodotExe or set NKG_GODOT_EXE."
}
if ([string]::IsNullOrWhiteSpace($GodotCppRoot)) {
    throw "godot-cpp root could not be resolved. Pass -GodotCppRoot."
}
if ([string]::IsNullOrWhiteSpace($LeanClrRoot)) {
    throw "LeanCLR root could not be resolved. Pass -LeanClrRoot."
}

if ($Clean) {
    dotnet clean $managedProject -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$buildArgs = @("build", $managedProject, "-c", $Configuration)
if ($Clean) {
    $buildArgs += "--no-incremental"
}
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not $SkipNativeBuild) {
    $nativeBuildArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $nativeBuildScript,
        "-Configuration", $Configuration
    )
    if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
        $nativeBuildArgs += @("-GodotExe", $GodotExe)
    }
    if (-not [string]::IsNullOrWhiteSpace($GodotCppRoot)) {
        $nativeBuildArgs += @("-GodotCppRoot", $GodotCppRoot)
    }
    if (-not [string]::IsNullOrWhiteSpace($LeanClrRoot)) {
        $nativeBuildArgs += @("-LeanClrRoot", $LeanClrRoot)
    }
    if ($Clean) {
        $nativeBuildArgs += "-Clean"
    }

    powershell @nativeBuildArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$managedOutputDir = Join-Path (Split-Path $managedProject -Parent) "bin\$Configuration\net10.0"
powershell -ExecutionPolicy Bypass -File $stageBclScript -ManagedAssemblyDir $managedOutputDir
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
