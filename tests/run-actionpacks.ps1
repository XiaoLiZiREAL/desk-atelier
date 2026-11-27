param([string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'),
      [string]$Output=(Join-Path $PSScriptRoot '..\build\action-pack-qa'),[string]$Pack='', [string]$HitoriPack='',
      [string]$RemPack='', [string]$KurisuPack='', [string]$MutsumiPack='')
$ErrorActionPreference='Stop'
$frame=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(!(Test-Path -LiteralPath $frame)){$frame=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$dir=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory($dir) | Out-Null
$exe=Join-Path $dir 'DeskAtelier.exe'
Copy-Item -LiteralPath $Program -Destination $exe -Force
$runner=Join-Path $dir 'ActionPackChecks.exe'
$argsForCompiler=@('/nologo','/target:exe','/codepage:65001',"/out:$runner","/r:$exe",'/r:System.IO.Compression.dll','/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll')
foreach($lib in 'WindowsBase','PresentationCore','PresentationFramework'){$argsForCompiler+="/r:$frame\WPF\$lib.dll"}
$argsForCompiler+=Join-Path $PSScriptRoot 'ActionPackChecks.cs'
& (Join-Path $frame 'csc.exe') @argsForCompiler
if($LASTEXITCODE){throw 'Action pack test build failed'}
$testArgs=@((Join-Path $dir ([Guid]::NewGuid().ToString('N'))))
if($Pack){$testArgs+=[IO.Path]::GetFullPath($Pack)}
if($HitoriPack){$testArgs+='--hitori';$testArgs+=[IO.Path]::GetFullPath($HitoriPack)}
if($RemPack){$testArgs+='--rem';$testArgs+=[IO.Path]::GetFullPath($RemPack)}
if($KurisuPack){$testArgs+='--kurisu';$testArgs+=[IO.Path]::GetFullPath($KurisuPack)}
if($MutsumiPack){$testArgs+='--mutsumi';$testArgs+=[IO.Path]::GetFullPath($MutsumiPack)}
& $runner @testArgs
if($LASTEXITCODE){throw 'Action pack verification failed'}
