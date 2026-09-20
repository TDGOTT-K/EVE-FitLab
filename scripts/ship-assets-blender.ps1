param([switch]$Prepare, [string]$Script)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$probeRoot = Join-Path $repoRoot 'output/ship-assets-probe'
$blenderExe = 'D:/Blender/blender5.1.2/blender.exe'
if (-not (Test-Path -LiteralPath $blenderExe)) { throw "Blender unavailable: $blenderExe" }
$savedConfig = $env:BLENDER_USER_CONFIG
$savedScripts = $env:BLENDER_USER_SCRIPTS
try {
    $env:BLENDER_USER_CONFIG = Join-Path $probeRoot 'blender-config'
    $env:BLENDER_USER_SCRIPTS = Join-Path $probeRoot 'blender-scripts'
    New-Item -ItemType Directory -Force -Path $env:BLENDER_USER_CONFIG,$env:BLENDER_USER_SCRIPTS | Out-Null
    if ($Prepare) {
        & $blenderExe --background --python-exit-code 1 --python (Join-Path $PSScriptRoot 'prepare-ship-assets.py')
        if ($LASTEXITCODE -ne 0) { throw "Asset preparation failed: $LASTEXITCODE" }
    } elseif ($Script) {
        & $blenderExe --background --python-exit-code 1 --python $Script
        if ($LASTEXITCODE -ne 0) { throw "Asset script failed: $LASTEXITCODE" }
    } else {
        # This command is an explicit interactive launcher for the user.
        & $blenderExe
    }
} finally {
    $env:BLENDER_USER_CONFIG = $savedConfig
    $env:BLENDER_USER_SCRIPTS = $savedScripts
}
