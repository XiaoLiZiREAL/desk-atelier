param([string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'), [string]$Output=(Join-Path $PSScriptRoot '..\build\qa'))
$ErrorActionPreference='Stop'
$frame=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(!(Test-Path -LiteralPath $frame)) { $frame=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$target=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory($target) | Out-Null
$retail=Join-Path $target 'DeskAtelier.exe'
Copy-Item -LiteralPath $Program -Destination $retail -Force
$harness=Join-Path $target 'Integration.exe'
$opts=@('/nologo','/target:exe','/codepage:65001',"/out:$harness","/reference:$retail",'/reference:System.dll','/reference:System.Core.dll','/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes') { $opts += "/reference:$frame\WPF\$name.dll" }
$opts+=Join-Path $PSScriptRoot 'Integration.cs'
& (Join-Path $frame 'csc.exe') @opts
if($LASTEXITCODE){throw 'Could not compile verification harness'}
& $harness $target
if($LASTEXITCODE){throw 'Integration check failed'}
