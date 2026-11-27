param([string]$Program=(Join-Path $PSScriptRoot '../build/DeskAtelier.exe'),
      [string]$Output=(Join-Path $PSScriptRoot '../build/paper-qa'),[switch]$CompileOnly,[switch]$PhysicalPointer)
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if(!(Test-Path -LiteralPath $fx)){$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'}
$dir=[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)).FullName
$retail=Join-Path $dir 'DeskAtelier.exe'
if((Resolve-Path -LiteralPath $Program).Path -ne $retail){Copy-Item -LiteralPath $Program -Destination $retail -Force}
$harness=Join-Path $dir 'PaperHandoffChecks.exe'
$refs=@('/nologo','/target:exe','/codepage:65001',"/out:$harness","/r:$retail",'/r:System.Xaml.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes'){$refs+="/r:$fx/WPF/$name.dll"}
& "$fx/csc.exe" @refs (Join-Path $PSScriptRoot 'PaperHandoffChecks.cs')
if($LASTEXITCODE){throw 'Paper handoff checks failed to compile'}
if(!$CompileOnly){
    $state=Join-Path $dir ('paper-'+[guid]::NewGuid().ToString('N'))
    $testArgs=@($state)
    if($PhysicalPointer){$testArgs+='--pointer'}
    & $harness @testArgs
    if($LASTEXITCODE){throw 'Paper handoff checks failed'}
    & $harness $state '--restart'
    if($LASTEXITCODE){throw 'Paper reminder restart checks failed'}
}
