param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
if ($version.Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or $version.PatchVersion -notmatch '^[0-9A-Za-z.-]+$') { throw 'Invalid version.json' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'build' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$dictionaryHash = (Get-FileHash -LiteralPath (Join-Path $root 'translations\ko.json') -Algorithm SHA256).Hash.ToLowerInvariant()
$stamp = @"
using System.Reflection;
[assembly: AssemblyVersion("$($version.Version).0")]
[assembly: AssemblyFileVersion("$($version.Version).0")]
[assembly: AssemblyInformationalVersion("$($version.Version)")]
namespace RisingKingdomsKO { internal static class BuildInfo {
public const string Version = "$($version.Version)";
public const string PatchVersion = "$($version.PatchVersion)";
public const string DictionarySha256 = "$dictionaryHash";
} }
"@
$stampPath = Join-Path $OutputDirectory 'BuildInfo.g.cs'
[IO.File]::WriteAllText($stampPath, $stamp, [Text.UTF8Encoding]::new($false))
& $compiler /nologo /target:winexe "/resource:$root\src\ScreenFill.conf,RisingKingdomsKO.ScreenFill.conf" /utf8output /out:"$OutputDirectory\RisingKingdomsLauncher.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$root\src\Launcher.cs" "$root\src\TextPatch.cs" "$root\src\Campaign.cs" "$root\src\Updater.cs" "$root\src\RuntimeContracts.cs" "$root\src\GameMonitor.cs" "$root\src\ScreenFill.cs" $stampPath
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (Test-Path (Join-Path $root 'translations\ko.json')) {
    New-Item -ItemType Directory -Force (Join-Path $OutputDirectory 'translations') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'translations\ko.json') -Destination (Join-Path $OutputDirectory 'translations\ko.json') -Force
}
Copy-Item -LiteralPath (Join-Path $root 'version.json') -Destination (Join-Path $OutputDirectory 'version.json') -Force
Write-Host "빌드 완료: $OutputDirectory\RisingKingdomsLauncher.exe (v$($version.Version))"
