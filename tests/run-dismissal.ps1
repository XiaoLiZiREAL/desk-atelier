param([string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'),
      [string]$Output=(Join-Path $PSScriptRoot '..\build\dismissal-qa'),[switch]$CompileOnly,
      [switch]$IncludeVisibleDesktop)
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $fx)) { $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$outDir=[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)).FullName
$retail=Join-Path $outDir 'DeskAtelier.exe'
Copy-Item -LiteralPath $Program -Destination $retail -Force
$exe=Join-Path $outDir 'DismissalChecks.exe'
$opts=@('/nologo','/target:exe','/codepage:65001',"/out:$exe","/r:$retail",'/r:System.Core.dll','/r:System.dll','/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationClient','UIAutomationTypes'){$opts+="/r:$fx\WPF\$name.dll"}
& "$fx\csc.exe" @opts (Join-Path $PSScriptRoot 'DismissalChecks.cs') (Join-Path $PSScriptRoot 'AttachmentClicks.cs')
if($LASTEXITCODE){throw 'Dismissal test build failed'}
if(!$CompileOnly){
    $testArgs=@($outDir)
    if($IncludeVisibleDesktop){$testArgs+='--desktop'}
    & $exe @testArgs
    if($LASTEXITCODE){throw 'Dismissal checks failed'}
}
