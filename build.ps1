$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'build') | Out-Null
& $compiler /nologo /target:winexe /utf8output /out:"$root\build\RisingKingdomsKO-Text.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$root\src\Launcher.cs" "$root\src\TextPatch.cs" "$root\src\Campaign.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (Test-Path (Join-Path $root 'translations\ko.json')) {
    New-Item -ItemType Directory -Force (Join-Path $root 'build\translations') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'translations\ko.json') -Destination (Join-Path $root 'build\translations\ko.json') -Force
}
Write-Host "빌드 완료: $root\build\RisingKingdomsKO-Text.exe"
