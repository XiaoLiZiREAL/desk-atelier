param([string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'),
      [string]$Output=(Join-Path $PSScriptRoot '..\build\dial-qa'),
      [switch]$CompileOnly,[switch]$PhysicalPointer)
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $fx)) { $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$dir=[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)).FullName
$exe=Join-Path $dir 'DeskAtelier.exe'
if ([IO.Path]::GetFullPath($Program) -ne $exe) {Copy-Item -LiteralPath $Program -Destination $exe -Force}
$runner=Join-Path $dir 'FocusDialChecks.exe'
$opts=@('/nologo','/optimize+','/target:exe','/codepage:65001',"/out:$runner","/r:$exe",'/r:System.Core.dll','/r:System.dll','/r:System.Xaml.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes','UIAutomationProvider'){$opts+="/r:$fx\WPF\$name.dll"}
& "$fx\csc.exe" @opts (Join-Path $PSScriptRoot 'FocusDialChecks.cs')
if($LASTEXITCODE){throw 'Dial test compilation failed'}
if(!$CompileOnly){
    $testArgs=@($dir)
    if($PhysicalPointer) { $testArgs+='--pointer' }
    & $runner @testArgs
    if($LASTEXITCODE){throw 'Dial test failed'}
}
