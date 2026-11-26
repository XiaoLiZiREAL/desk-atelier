param(
    [string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'),
    [string]$Output=(Join-Path $PSScriptRoot '..\build\music-qa'),
    [switch]$CompileOnly
)
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $fx)) {$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$metadata=Join-Path $env:WINDIR 'System32\WinMetadata'
if([Environment]::Is64BitOperatingSystem -and ![Environment]::Is64BitProcess){$metadata=Join-Path $env:WINDIR 'Sysnative\WinMetadata'}
$target=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory($target) | Out-Null
$retail=Join-Path $target 'DeskAtelier.exe'
Copy-Item -LiteralPath $Program -Destination $retail -Force
$refs=@('/nologo','/target:exe','/codepage:65001','/utf8output',"/r:$retail",'/r:System.Core.dll','/r:System.dll','/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes','UIAutomationClient','UIAutomationProvider'){$refs+="/r:$fx\WPF\$name.dll"}
foreach($name in 'System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Collections'){$refs+="/r:$fx\$name.dll"}
foreach($name in 'Windows.Foundation','Windows.Media','Windows.Storage'){$refs+="/r:$metadata\$name.winmd"}
$harness=Join-Path $target 'MusicIntegration.exe'
$fixture=Join-Path $target 'QQMusic.exe'
& "$fx\csc.exe" @refs "/out:$harness" '/main:MusicChecks' (Join-Path $PSScriptRoot 'Integration.cs') (Join-Path $PSScriptRoot 'MusicIntegration.cs') (Join-Path $PSScriptRoot 'NativeTestSong.cs')
if($LASTEXITCODE){throw 'Music integration harness compile failed'}
& "$fx\csc.exe" @refs "/out:$fixture" (Join-Path $PSScriptRoot 'media-probes\WindowFixture.cs')
if($LASTEXITCODE){throw 'UI accessibility fixture compile failed'}
if(!$CompileOnly){
    & $harness $target $fixture
    if($LASTEXITCODE){throw 'Music integration checks failed'}
}
