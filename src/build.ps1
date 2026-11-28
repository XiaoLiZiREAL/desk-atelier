param([string]$Output = (Join-Path $PSScriptRoot '..\build\DeskAtelier.exe'), [switch]$Preview)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$outPath = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outPath)) | Out-Null
$params = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output','/codepage:65001',"/out:$outPath",
  "/win32icon:$PSScriptRoot\assets\app.ico", "/win32manifest:$PSScriptRoot\app.manifest",
  "/resource:$PSScriptRoot\QuickMenu.xaml,DeskMate.QuickMenu.xaml",
  "/resource:$PSScriptRoot\CardTheme.xaml,DeskMate.CardTheme.xaml",
  '/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll','/reference:System.Xaml.dll',
  '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.Web.Extensions.dll')
if ($Preview) { $params += '/define:DESKATELIER_PREVIEW' }
foreach ($lib in 'WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationProvider.dll','UIAutomationTypes.dll') { $params += "/reference:$framework\WPF\$lib" }
foreach ($lib in 'System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Collections.dll') { $params += "/reference:$framework\$lib" }
$systemMetadata = Join-Path $env:WINDIR 'System32\WinMetadata'
if ([Environment]::Is64BitOperatingSystem -and $framework -notmatch 'Framework64') { $systemMetadata = Join-Path $env:WINDIR 'Sysnative\WinMetadata' }
foreach ($lib in 'Windows.Media.winmd','Windows.Foundation.winmd','Windows.Graphics.winmd','Windows.Globalization.winmd','Windows.Storage.winmd') { $params += "/reference:$systemMetadata\$lib" }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File) { $params += ('/resource:' + $file.FullName + ',art.' + $file.Name) }
foreach ($role in 'hitori','mutsumi') {
    $art=Join-Path $PSScriptRoot "../art/$role"
    $params += "/resource:$art/atlas.png,art.$role.png"
    $params += "/resource:$art/portrait.png,art.$role-still.png"
}
$stageArt=Join-Path $PSScriptRoot '../art/mutsumi/outfits/stage'
$params += "/resource:$stageArt/atlas.png,art.mutsumi-stage.png"
$params += "/resource:$stageArt/portrait.png,art.mutsumi-stage-still.png"
foreach ($strip in Get-ChildItem -LiteralPath (Join-Path $stageArt 'actions') -Filter '*.png' -File) {
    $params += "/resource:$($strip.FullName),art.motion-mutsumi-stage-$($strip.Name)"
}
foreach ($role in 'hitori','rem','kurisu','mutsumi') {
    foreach ($strip in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "../art/$role/actions") -Filter '*.png' -File) {
        $asset="motion-$role-"+$strip.Name
        if (!(Test-Path -LiteralPath (Join-Path "$PSScriptRoot/assets" $asset))) {
            $params += "/resource:$($strip.FullName),art.$asset"
        }
    }
}
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs') { $params += $file.FullName }
& (Join-Path $framework 'csc.exe') @params
if ($LASTEXITCODE) { throw "Compilation failed ($LASTEXITCODE)" }
Write-Output $outPath
