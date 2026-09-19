$env:FITLAB_NENGINE_ROOT='D:/AI/GPT6/N号引擎-维护-0.201-r45'
$env:FITLAB_NENGINE_STATE='D:/AI/GPT6/EVE-FitLab/output/integration-r54-ui-001/native'
$env:FITLAB_STORAGE_CONFIG='D:/AI/GPT6/EVE-FitLab/output/integration-r54-ui-001/storage.json'
$env:FITLAB_DEFAULT_STATE='D:/AI/GPT6/EVE-FitLab/output/integration-r54-ui-001/state'
$env:FITLAB_API_PORT='56454'
Set-Location (Split-Path $PSScriptRoot)
python run.py
