param([string]$EngineSourceRoot='',[Parameter(Mandatory=$true)][string]$SdeRoot)
$ErrorActionPreference='Stop'
if(-not $EngineSourceRoot){$EngineSourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../engine'))}
Set-Location (Split-Path $PSScriptRoot -Parent)
python desktop/prepare.py --engine $EngineSourceRoot --sde $SdeRoot
if($LASTEXITCODE){throw 'Resource preparation failed'}
dotnet publish desktop/engine/FitLab.Engine.csproj -c Release -r win-x64 --self-contained true -o desktop/build/engine "-p:EngineSourceRoot=$EngineSourceRoot"
if($LASTEXITCODE){throw 'Engine publish failed'}
python -m PyInstaller --noconfirm --clean --onedir --name FitLab.Backend --paths . --distpath desktop/build/python --workpath desktop/build/py-work --specpath desktop/build desktop/backend_entry.py
if($LASTEXITCODE){throw 'Backend packaging failed'}
New-Item -ItemType Directory -Force desktop/build/backend | Out-Null
Copy-Item -Path desktop/build/python/FitLab.Backend/* -Destination desktop/build/backend -Recurse -Force
npm run package:windows
if($LASTEXITCODE){throw 'Windows installer packaging failed'}
