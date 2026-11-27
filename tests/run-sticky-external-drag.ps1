param([string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'),
      [string]$Output=(Join-Path $PSScriptRoot '..\build\sticky-drag-qa'))
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(!(Test-Path -LiteralPath $fx)){$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$dir=[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)).FullName
$retail=Join-Path $dir 'DeskAtelier.exe'
if((Resolve-Path -LiteralPath $Program).Path -ne $retail){Copy-Item -LiteralPath $Program -Destination $retail -Force}
$harness=Join-Path $dir 'StickyExternalDragChecks.exe'
$launcher=Join-Path $dir 'RunOffDesktop.exe'
$refs=@('/nologo','/target:exe','/codepage:65001',"/out:$harness","/r:$retail",'/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes'){$refs+="/r:$fx\WPF\$name.dll"}
& "$fx\csc.exe" @refs (Join-Path $PSScriptRoot 'StickyExternalDragChecks.cs')
if($LASTEXITCODE){throw 'Sticky drag checks failed to compile'}
& "$fx\csc.exe" /nologo /target:exe "/out:$launcher" (Join-Path $PSScriptRoot 'RunOffDesktop.cs')
if($LASTEXITCODE){throw 'Private desktop launcher failed to compile'}
$state=Join-Path $dir ('state-'+[guid]::NewGuid().ToString('N'))
& $launcher $harness $state
if($LASTEXITCODE){throw 'Sticky external drag checks failed'}
