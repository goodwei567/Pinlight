$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', '/codepage:65001', ('/out:' + (Join-Path $build 'Pinlight.exe')), ('/win32manifest:' + (Join-Path $root 'app.manifest')), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll')
$icon = Join-Path $root 'assets\Pinlight.ico'
if (Test-Path -LiteralPath $icon) { $arguments += '/win32icon:' + $icon }
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output (Join-Path $build 'Pinlight.exe')
