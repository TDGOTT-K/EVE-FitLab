param([int]$Port=5208,[string]$EngineRoot='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if($EngineRoot){$env:FITLAB_NENGINE_ROOT=(Resolve-Path -LiteralPath $EngineRoot).Path}
elseif(!$env:FITLAB_NENGINE_ROOT){$env:FITLAB_NENGINE_ROOT=Join-Path (Split-Path $projectRoot -Parent) 'NEngine'}
$env:FITLAB_DEFAULT_STATE=Join-Path $projectRoot 'state'
$env:FITLAB_STORAGE_CONFIG=Join-Path $projectRoot 'state/storage.json'
$env:FITLAB_NENGINE_STATE=Join-Path $projectRoot 'state/native'
$env:FITLAB_API_PORT=[string]$Port
$env:FITLAB_CALCULATOR='nengine'
Push-Location $projectRoot
try { python run.py } finally { Pop-Location }
