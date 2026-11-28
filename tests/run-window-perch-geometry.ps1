param([string]$Output=(Join-Path $PSScriptRoot '..\build\WindowPerchGeometryChecks.exe'))
$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $fx)) { $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$exe=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($exe)) | Out-Null
& (Join-Path $fx 'csc.exe') /nologo /target:exe "/out:$exe" (Join-Path $PSScriptRoot '..\src\WindowPerchGeometry.cs') (Join-Path $PSScriptRoot '..\src\WindowPerchMotion.cs') (Join-Path $PSScriptRoot '..\src\ProjectPlantTimeline.cs') (Join-Path $PSScriptRoot 'WindowPerchGeometryChecks.cs')
if ($LASTEXITCODE) { throw "Window perch geometry compile failed ($LASTEXITCODE)" }
& $exe
if ($LASTEXITCODE) { throw "Window perch geometry checks failed ($LASTEXITCODE)" }
