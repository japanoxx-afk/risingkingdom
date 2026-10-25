param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'build' }
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory
$testProcess = Start-Process -FilePath (Join-Path $OutputDirectory 'RisingKingdomsLauncher.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($testProcess.ExitCode -ne 0) { throw "Self-test failed (exit $($testProcess.ExitCode))" }
Write-Host 'PASS: install, restore, interrupted recovery, traversal, incomplete package, corruption, external changes'
