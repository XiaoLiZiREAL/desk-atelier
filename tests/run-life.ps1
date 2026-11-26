param(
    [string]$Program=(Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'),
    [string]$Output=(Join-Path $PSScriptRoot '..\build\life-qa'),
    [switch]$RequireSteve,
    [switch]$CompileOnly
)
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $fx)) { $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$folder=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory($folder) | Out-Null
$retail=Join-Path $folder 'DeskAtelier.exe'
if([IO.Path]::GetFullPath($Program) -ne $retail){Copy-Item -LiteralPath $Program -Destination $retail -Force}
$opts=@('/nologo','/optimize+','/target:exe','/codepage:65001','/utf8output',"/r:$retail",'/r:System.Core.dll','/r:System.dll','/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
foreach($name in 'WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes'){$opts+="/r:$fx\WPF\$name.dll"}
$exe=Join-Path $folder 'LifeChecks.exe'
& "$fx\csc.exe" @opts "/out:$exe" (Join-Path $PSScriptRoot 'LifeChecks.cs')
if($LASTEXITCODE){throw 'Life UI verification harness failed to compile'}
if(!$CompileOnly){
    & $exe $folder $RequireSteve.IsPresent
    if($LASTEXITCODE){throw 'Life UI checks failed; see life-report.txt'}
}
