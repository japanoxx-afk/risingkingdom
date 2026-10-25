$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'build') | Out-Null
& $compiler /nologo /target:winexe /utf8output /out:"$root\build\RisingKingdomsKO.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$root\src\Launcher.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "빌드 완료: $root\build\RisingKingdomsKO.exe"
