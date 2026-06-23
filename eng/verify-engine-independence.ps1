$ErrorActionPreference = "Stop"

$pattern = "UnityEngine|UnityEditor|MonoBehaviour|GameObject|Transform|using\s+Godot|YooAsset|HybridCLR|Luban"
$paths = @(
    "src/NKGGameFramework",
    "tests"
)
$matches = & rg --line-number $pattern $paths

if ($LASTEXITCODE -eq 0) {
    $matches
    Write-Error "Engine-specific dependency found outside adapter projects."
    exit 1
}

if ($LASTEXITCODE -eq 1) {
    Write-Host "No engine-specific dependency found outside adapter projects."
    exit 0
}

exit $LASTEXITCODE
