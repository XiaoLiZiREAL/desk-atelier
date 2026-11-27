param([string]$Program=(Join-Path $PSScriptRoot '../build/DeskAtelier.exe'),
      [string]$Output=(Join-Path $PSScriptRoot '../build/duet-qa'),
      [switch]$CompileOnly,[switch]$Capture)
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if(!(Test-Path -LiteralPath $fx)){$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'}
$dir=[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)).FullName
$retail=Join-Path $dir 'DeskAtelier.exe'
if((Get-Item -LiteralPath $Program).FullName -ne $retail){Copy-Item -LiteralPath $Program -Destination $retail -Force}
$harness=Join-Path $dir 'DuetChecks.exe'
$refs=@('/nologo','/target:exe','/codepage:65001',"/out:$harness","/r:$retail",'/r:System.Xaml.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes'){$refs+="/r:$fx/WPF/$name.dll"}
& "$fx/csc.exe" @refs (Join-Path $PSScriptRoot 'DuetChecks.cs')
if($LASTEXITCODE){throw 'Duet checks failed to compile'}
if(!$CompileOnly){
    $testArgs=@($dir)
    if($Capture){$testArgs+='--frames'}
    & $harness @testArgs
    if($LASTEXITCODE){throw 'Duet checks failed'}
}
