param([string]$Output=(Join-Path $env:TEMP 'DeskAtelier-FocusCompanionChecks.exe'))
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $fx)) { $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$source=Join-Path $PSScriptRoot '..\src\FocusCompanionCue.cs'
$test=Join-Path $PSScriptRoot 'FocusCompanionChecks.cs'
$mine=Join-Path $PSScriptRoot '..\src\MineShift.cs'
& (Join-Path $fx 'csc.exe') /nologo /target:exe /codepage:65001 "/out:$Output" $source $mine $test
if ($LASTEXITCODE) { throw 'Focus companion test compilation failed' }
& $Output
if ($LASTEXITCODE) { throw 'Focus companion checks failed' }
