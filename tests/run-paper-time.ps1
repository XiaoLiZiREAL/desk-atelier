param([string]$Output=(Join-Path $PSScriptRoot '../build/paper-time-qa'))
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if(!(Test-Path -LiteralPath $fx)){$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'}
$target=[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)).FullName
$checks=Join-Path $target 'PaperTimeChecks.exe'
& "$fx/csc.exe" /nologo /utf8output /codepage:65001 "/out:$checks" (Join-Path $PSScriptRoot '../src/PaperTime.cs') (Join-Path $PSScriptRoot 'PaperTimeChecks.cs')
if($LASTEXITCODE){throw 'Time recognition tests failed to compile'}
& $checks
if($LASTEXITCODE){throw 'Time recognition checks failed'}
