param([string]$ReleaseDirectory = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
if (!$ReleaseDirectory) {
    $ReleaseDirectory = Join-Path $root ('build\updater-test-' + [guid]::NewGuid().ToString('N'))
    & (Join-Path $root 'package-release.ps1') -OutputDirectory $ReleaseDirectory
}
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifest = Join-Path $ReleaseDirectory 'update.json'
$zip = Join-Path $ReleaseDirectory "RisingKingdomsKO-$($version.Version)-win.zip"
$launcher = Join-Path $ReleaseDirectory 'payload\RisingKingdomsLauncher.exe'
$stdout = Join-Path $ReleaseDirectory 'test.log'
$stderr = Join-Path $ReleaseDirectory 'test-error.log'
$arguments = '--updater-self-test "' + $zip + '" "' + $manifest + '"'
$testProcess = Start-Process -FilePath $launcher -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Get-Content -LiteralPath $stdout
if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw "Updater self-test failed (exit $($testProcess.ExitCode))" }
$handoffRoot = Join-Path $ReleaseDirectory 'handoff-test'
$success = Join-Path $handoffRoot 'success'
$failure = Join-Path $handoffRoot 'failure'
New-Item -ItemType Directory -Force $success,$failure | Out-Null
$literalRoot = $handoffRoot.Replace('"','""')
$fixture = @"
using System;
using System.IO;
class Fixture {
static int Main(string[] args) {
if (AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).EndsWith("failure")) return 1;
string path = Path.Combine(@"$literalRoot", "handoffs");
Directory.CreateDirectory(path);
File.WriteAllText(Path.Combine(path, args[1]+".ready"), "test");
return 0;
} }
"@
$source = Join-Path $handoffRoot 'Fixture.cs'
[IO.File]::WriteAllText($source,$fixture,[Text.UTF8Encoding]::new($false))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /out:"$success\RisingKingdomsLauncher.exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Handoff fixture build failed' }
Copy-Item -LiteralPath (Join-Path $success 'RisingKingdomsLauncher.exe') -Destination (Join-Path $failure 'RisingKingdomsLauncher.exe')
$arguments = '--test-handoff "' + $handoffRoot + '" "' + $success + '" "' + $failure + '"'
$testProcess = Start-Process -FilePath $launcher -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $ReleaseDirectory 'handoff.log') -RedirectStandardError (Join-Path $ReleaseDirectory 'handoff-error.log')
Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'handoff.log')
if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'handoff-error.log'); throw 'Handoff integration test failed' }
