param(
    [string]$Configuration = "Release",
    [string]$GodotCppRoot,
    [string]$LeanClrRoot,
    [string]$GodotExe,
    [string]$BuildDir,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Resolve-CMakePath {
    $cmake = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -ne $cmake) {
        return $cmake.Source
    }

    $candidatePaths = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidatePaths += Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\*\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe") -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidatePaths += Get-ChildItem -Path (Join-Path $env:ProgramFiles "Microsoft Visual Studio\2022\*\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe") -ErrorAction SilentlyContinue
    }

    $candidate = $candidatePaths | Sort-Object FullName | Select-Object -First 1
    if ($null -eq $candidate) {
        throw "cmake was not found in PATH or Visual Studio 2022 Build Tools."
    }

    return $candidate.FullName
}

function Assert-SafeCleanPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$AllowedRoot
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if (-not ($fullPath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
        throw "Refusing to clean build directory outside '$fullRoot': $fullPath"
    }

    return $fullPath
}

$nativeRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $nativeRoot "..")).Path
$repoRoot = (Resolve-Path (Join-Path $nativeRoot "..\..\..")).Path
$ensureScript = Join-Path $projectRoot "tools\ensure-godot-4.7.ps1"

$ensureArgs = @("-ExecutionPolicy", "Bypass", "-File", $ensureScript)
if (-not [string]::IsNullOrWhiteSpace($GodotCppRoot)) {
    $ensureArgs += @("-GodotCppRoot", $GodotCppRoot)
}
if (-not [string]::IsNullOrWhiteSpace($LeanClrRoot)) {
    $ensureArgs += @("-LeanClrRoot", $LeanClrRoot)
}
if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
    $ensureArgs += @("-GodotExe", $GodotExe)
}

$toolInfo = (& powershell @ensureArgs | Select-Object -Last 1) | ConvertFrom-Json
$GodotCppRoot = $toolInfo.GodotCppRoot
$LeanClrRoot = $toolInfo.LeanClrRoot
$GodotExe = $toolInfo.GodotExe

if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = Join-Path $repoRoot "out\godot-plane-gdextension"
}

if ($Clean -and (Test-Path $BuildDir)) {
    $safeBuildDir = Assert-SafeCleanPath -Path $BuildDir -AllowedRoot (Join-Path $repoRoot "out")
    Write-Host "Cleaning native build directory $safeBuildDir"
    Remove-Item -LiteralPath $safeBuildDir -Recurse -Force
}

$apiDir = Join-Path (Split-Path $repoRoot -Parent) ".cache\godot-api-4.7"
New-Item -ItemType Directory -Force $apiDir | Out-Null
$apiFile = Join-Path $apiDir "extension_api.json"
if (-not (Test-Path $apiFile)) {
    Push-Location $apiDir
    try {
        Invoke-Checked $GodotExe "--headless" "--dump-extension-api"
    }
    finally {
        Pop-Location
    }
}

$cmakePath = Resolve-CMakePath
$configureArgs = @(
    "-S", $nativeRoot,
    "-B", $BuildDir,
    "-G", "Visual Studio 17 2022",
    "-A", "x64",
    "-DGODOT_CPP_ROOT=$GodotCppRoot",
    "-DLEANCLR_ROOT=$LeanClrRoot",
    "-DGODOTCPP_CUSTOM_API_FILE=$apiFile",
    "-DNKG_GODOT_PROJECT_DIR=$projectRoot"
)

Invoke-Checked $cmakePath @configureArgs
Invoke-Checked $cmakePath "--build" $BuildDir "--config" $Configuration "--target" "nkg_leanclr_bridge" "--parallel"

$godotCacheDir = Join-Path $projectRoot ".godot"
New-Item -ItemType Directory -Force $godotCacheDir | Out-Null
Set-Content -Encoding UTF8 `
    -Path (Join-Path $godotCacheDir "extension_list.cfg") `
    -Value "res://addons/nkg_leanclr_bridge/nkg_leanclr_bridge.gdextension"
