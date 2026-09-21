param([string]$EngineSourceRoot='',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if($EngineSourceRoot){python desktop/prepare_nengine.py --engine $EngineSourceRoot}else{python desktop/prepare_nengine.py}
if($LASTEXITCODE){throw 'Pinned engine resource preparation failed'}
python -m PyInstaller --noconfirm --clean --onedir --name FitLab.Backend --paths . --distpath desktop/build/python --workpath desktop/build/py-work --specpath desktop/build desktop/backend_entry.py
if($LASTEXITCODE){throw 'Backend packaging failed'}
New-Item -ItemType Directory -Force desktop/build/backend | Out-Null
Copy-Item -Path desktop/build/python/FitLab.Backend/* -Destination desktop/build/backend -Recurse -Force
python -m PyInstaller --noconfirm --clean --onedir --name FitLab.Agent --paths . --distpath desktop/build/agent-python --workpath desktop/build/agent-py-work --specpath desktop/build desktop/agent_entry.py
if($LASTEXITCODE){throw 'Agent packaging failed'}
Copy-Item -LiteralPath desktop/build/agent-python/FitLab.Agent -Destination desktop/build/agent -Recurse
Copy-Item -LiteralPath skills -Destination desktop/build/agent/skills -Recurse
Copy-Item -LiteralPath prompts -Destination desktop/build/agent/prompts -Recurse
Copy-Item -LiteralPath desktop/build/nengine/src/NEngine.Mcp/bin/Debug/net10.0 -Destination desktop/build/nengine/agent-runtime -Recurse
Copy-Item -LiteralPath desktop/build/nengine/UI-LOCAL-BASELINE.json -Destination desktop/build/nengine/UI-CHARGE-FEEDBACK-BASELINE.json
if($OutputDirectory){node node_modules/electron-builder/cli.js --win nsis --x64 "--config.directories.output=$OutputDirectory"}else{npm run package:windows}
if($LASTEXITCODE){throw 'Windows installer packaging failed'}
