param(
    [string]$GodotCppRoot,
    [string]$GodotExe,
    [string]$LeanClrRoot,
    [switch]$SkipGodotCpp,
    [switch]$SkipGodotExe
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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$workspaceRoot = (Resolve-Path (Join-Path $repoRoot "..")).Path
$cacheRoot = Join-Path $workspaceRoot ".cache"

if ([string]::IsNullOrWhiteSpace($GodotCppRoot)) {
    $GodotCppRoot = Join-Path $cacheRoot "godot-cpp"
}
if ([string]::IsNullOrWhiteSpace($LeanClrRoot)) {
    $LeanClrRoot = Join-Path $workspaceRoot "leanclr"
}
if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = Join-Path (Join-Path $cacheRoot "godot\4.7-stable") "Godot_v4.7-stable_win64_console.exe"
}

$GodotCppRoot = [System.IO.Path]::GetFullPath($GodotCppRoot)
$GodotExe = [System.IO.Path]::GetFullPath($GodotExe)
$LeanClrRoot = [System.IO.Path]::GetFullPath($LeanClrRoot)

if (-not (Test-Path (Join-Path $LeanClrRoot "src\runtime\CMakeLists.txt"))) {
    throw "LeanCLR root was not found: $LeanClrRoot"
}

if (-not $SkipGodotCpp) {
    if (-not (Test-Path (Join-Path $GodotCppRoot "CMakeLists.txt"))) {
        New-Item -ItemType Directory -Force (Split-Path $GodotCppRoot -Parent) | Out-Null
        Write-Host "Cloning official godot-cpp into $GodotCppRoot"
        Invoke-Checked git clone --depth 1 https://github.com/godotengine/godot-cpp.git $GodotCppRoot
    }
    elseif (Test-Path (Join-Path $GodotCppRoot ".git")) {
        Write-Host "Updating official godot-cpp at $GodotCppRoot"
        Invoke-Checked git -C $GodotCppRoot checkout master
        Invoke-Checked git -C $GodotCppRoot pull --ff-only origin master
    }
}

if (-not $SkipGodotExe -and -not (Test-Path $GodotExe)) {
    $godotDir = Split-Path $GodotExe -Parent
    New-Item -ItemType Directory -Force $godotDir | Out-Null

    $zipPath = Join-Path $godotDir "Godot_v4.7-stable_win64.exe.zip"
    $downloadUrl = "https://github.com/godotengine/godot-builds/releases/download/4.7-stable/Godot_v4.7-stable_win64.exe.zip"
    Write-Host "Downloading official Godot 4.7 stable from $downloadUrl"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Force -Path $zipPath -DestinationPath $godotDir
}

if (-not $SkipGodotExe -and -not (Test-Path $GodotExe)) {
    throw "Godot 4.7 executable was not found after setup: $GodotExe"
}

[pscustomobject]@{
    GodotVersion = "4.7-stable"
    GodotCppRef = "master"
    GodotCppRoot = $GodotCppRoot
    GodotExe = $GodotExe
    LeanClrRoot = $LeanClrRoot
} | ConvertTo-Json -Compress
