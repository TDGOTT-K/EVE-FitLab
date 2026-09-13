$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$workspaceRoot=(Get-Location).Path
$installPath=[IO.Path]::GetFullPath((Join-Path $workspaceRoot 'output/installed-desktop-test'))
if(-not $installPath.StartsWith((Join-Path $workspaceRoot 'output')+[IO.Path]::DirectorySeparatorChar)){throw 'Invalid test installation path'}
$releaseVersion=(Get-Content package.json -Raw -Encoding UTF8 | ConvertFrom-Json).version
$installer=(Resolve-Path ("release/EVE-FitLab-$releaseVersion-Windows-x64-Setup.exe")).Path
$process=Start-Process -FilePath $installer -ArgumentList '/S',"/D=$installPath" -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw "Installer failed: $($process.ExitCode)"}
if(-not(Test-Path -LiteralPath (Join-Path $installPath 'EVE FitLab.exe'))){throw 'Installed application missing'}
$env:FITLAB_TEST_ROOT=Join-Path $workspaceRoot 'output/installed-desktop-data'
$smokeStarted=Get-Date
$smoke=Start-Process -FilePath (Join-Path $installPath 'EVE FitLab.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
if((Get-Item (Join-Path $env:FITLAB_TEST_ROOT 'desktop-smoke.json')).LastWriteTime -lt $smokeStarted){throw 'Smoke result was not refreshed'}
$result=Get-Content -Encoding UTF8 -Raw (Join-Path $env:FITLAB_TEST_ROOT 'desktop-smoke.json') | ConvertFrom-Json
if($result.cpu -ne 162.5 -or $result.unauthorizedStatus -ne 403){throw 'Installed application smoke failed'}
$uninstaller=Join-Path $installPath 'Uninstall EVE FitLab.exe'
$removed=Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -Wait -PassThru
@{installed=$true;cpu=$result.cpu;unauthorizedStatus=$result.unauthorizedStatus;uninstallExit=$removed.ExitCode;dataPreserved=(Test-Path -LiteralPath (Join-Path $env:FITLAB_TEST_ROOT 'Data'))} | ConvertTo-Json | Set-Content -Encoding UTF8 output/desktop-install-result.json
