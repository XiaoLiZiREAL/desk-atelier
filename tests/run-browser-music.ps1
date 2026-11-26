param(
    [string]$Program=(Join-Path $PSScriptRoot '../build/DeskAtelier.exe'),
    [string]$Output=(Join-Path $PSScriptRoot '../build/browser-music-qa'),
    [switch]$CompileOnly
)
$ErrorActionPreference='Stop'
$run=Join-Path ([IO.Path]::GetFullPath($Output)) ('run-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$retail=Join-Path $run 'DeskAtelier.exe'
Copy-Item -LiteralPath $Program -Destination $retail
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if(!(Test-Path -LiteralPath $framework)){$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'}
$harness=Join-Path $run 'BrowserMusicChecks.exe'
$refs=@('/nologo','/target:exe','/utf8output','/codepage:65001',"/r:$retail",'/r:System.Xaml.dll')
foreach($lib in 'PresentationFramework','PresentationCore','WindowsBase'){$refs+="/r:$framework/WPF/$lib.dll"}
& "$framework/csc.exe" @refs "/out:$harness" (Join-Path $PSScriptRoot 'BrowserMusicChecks.cs')
if($LASTEXITCODE){throw 'Browser presentation checks failed to compile'}
$siteChecks=Join-Path $run 'NetEaseBrowserChecks.exe'
& "$framework/csc.exe" @refs "/out:$siteChecks" (Join-Path $PSScriptRoot 'NetEaseBrowserChecks.cs')
if($LASTEXITCODE){throw 'NetEase web player checks failed to compile'}
if(!$CompileOnly){
    & $siteChecks
    if($LASTEXITCODE){throw 'NetEase web player checks failed'}
    & $harness $run
    if($LASTEXITCODE){throw 'Browser presentation checks failed'}
}
