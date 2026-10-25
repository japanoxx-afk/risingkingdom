$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$testProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'build\RisingKingdomsLauncher.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($testProcess.ExitCode -ne 0) { throw "Self-test failed (exit $($testProcess.ExitCode))" }
Write-Host 'PASS: install, restore, interrupted recovery, traversal, incomplete package, corruption, external changes'
