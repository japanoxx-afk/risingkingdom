param([string]$Game = 'C:\Program Files (x86)\Steam\steamapps\common\Rising Kingdoms')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'test.ps1')
$testRoot = Join-Path $PSScriptRoot ('build\integration-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot 'game'
$package = Join-Path $testRoot 'package'
New-Item -ItemType Directory -Force (Join-Path $fixture 'Packs'),(Join-Path $fixture 'Local\Fonts'),(Join-Path $fixture 'adventures') | Out-Null
$relativeFiles = @('Packs\data.pak') + @(Get-ChildItem -LiteralPath (Join-Path $Game 'Local\Fonts') -Filter '*.apf' | ForEach-Object { 'Local\Fonts\' + $_.Name })
$relativeFiles += @('adventures\Darkling Campaign.bfhp','adventures\Forester Campaign.bfhp','adventures\Human Campaign.bfhp','adventures\Tutorial.BFHP')
$hashes = @{}
foreach ($relative in $relativeFiles) {
    $source = Join-Path $Game $relative
    $hashes[$relative] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $source -Destination (Join-Path $fixture $relative)
}
Set-Content -LiteralPath (Join-Path $fixture 'RK.exe') -Value 'Test fixture only; never executed.'
$launcher = Join-Path $PSScriptRoot 'build\RisingKingdomsKO-Text.exe'
function Invoke-LauncherTest([string]$TestName,[string[]]$Arguments) {
    $stdout = Join-Path $testRoot ($TestName + '.log')
    $stderr = Join-Path $testRoot ($TestName + '-error.log')
    $quoted = @($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $testProcess = Start-Process -FilePath $launcher -ArgumentList $quoted -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Get-Content -LiteralPath $stdout
    if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw "$TestName failed" }
}
Invoke-LauncherTest 'build' @('--build-text',$fixture,(Join-Path $PSScriptRoot 'translations\ko.json'),$package)
Invoke-LauncherTest 'apply' @('--apply',$fixture,$package)
foreach ($relative in $relativeFiles) {
    if ((Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash -eq $hashes[$relative]) { throw "Patch did not change $relative" }
}
Invoke-LauncherTest 'restore' @('--restore',$fixture)
foreach ($relative in $relativeFiles) {
    if ((Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash -ne $hashes[$relative]) { throw "Restore failed: $relative" }
    if ((Get-FileHash -LiteralPath (Join-Path $Game $relative)).Hash -ne $hashes[$relative]) { throw "Original installation changed: $relative" }
}
Write-Host "PASS: PAK/campaign/font copies built, eleven-file install and exact restore. Original installation unchanged. Results: $testRoot"
