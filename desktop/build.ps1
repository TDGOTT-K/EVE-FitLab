param([string]$EngineSourceRoot='')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if($EngineSourceRoot){python desktop/prepare_nengine.py --engine $EngineSourceRoot}else{python desktop/prepare_nengine.py}
if($LASTEXITCODE){throw 'Pinned engine resource preparation failed'}
python -m PyInstaller --noconfirm --clean --onedir --name FitLab.Backend --paths . --distpath desktop/build/python --workpath desktop/build/py-work --specpath desktop/build desktop/backend_entry.py
if($LASTEXITCODE){throw 'Backend packaging failed'}
New-Item -ItemType Directory -Force desktop/build/backend | Out-Null
Copy-Item -Path desktop/build/python/FitLab.Backend/* -Destination desktop/build/backend -Recurse -Force
npm run package:windows
if($LASTEXITCODE){throw 'Windows installer packaging failed'}
