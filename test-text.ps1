param([string]$Game = 'C:\Program Files (x86)\Steam\steamapps\common\Rising Kingdoms', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'build' }
& (Join-Path $PSScriptRoot 'test.ps1') -OutputDirectory $OutputDirectory
$testRoot = Join-Path $PSScriptRoot ('build\integration-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot 'game'
$package = Join-Path $testRoot 'package'
New-Item -ItemType Directory -Force (Join-Path $fixture 'Packs'),(Join-Path $fixture 'Local\Fonts'),(Join-Path $fixture 'adventures') | Out-Null
$relativeFiles = @('Packs\data.pak') + @(Get-ChildItem -LiteralPath (Join-Path $Game 'Local\Fonts') -Filter '*.apf' | ForEach-Object { 'Local\Fonts\' + $_.Name })
$relativeFiles += @('adventures\Darkling Campaign.bfhp','adventures\Forester Campaign.bfhp','adventures\Human Campaign.bfhp','adventures\Tutorial.BFHP')
$hashes = @{}
$installationHashes = @{}
$backupRoot = Join-Path $Game '.risingkingdom-ko'
$backup = $null
if (Test-Path -LiteralPath (Join-Path $backupRoot 'ready')) {
    $backup = Get-Content -LiteralPath (Join-Path $backupRoot 'backup.json') -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($backup.Game) -ne [IO.Path]::GetFullPath($Game)) { throw 'Backup game path mismatch' }
    Write-Host 'Installed patch detected. Reading verified originals from its backup; the active game stays unchanged.'
}
foreach ($relative in $relativeFiles) {
    $source = Join-Path $Game $relative
    $installationHashes[$relative] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($backup) {
        $record = @($backup.Files | Where-Object { $_.Path.Replace('/','\') -eq $relative })
        if ($record.Count -ne 1) { throw "Missing original backup record: $relative" }
        $source = Join-Path (Join-Path $backupRoot 'original') $relative
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $record[0].OriginalSha256) { throw "Original backup damaged: $relative" }
    }
    $hashes[$relative] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $source -Destination (Join-Path $fixture $relative)
}
$exeHash = (Get-FileHash -LiteralPath (Join-Path $Game 'RK.exe') -Algorithm SHA256).Hash
Copy-Item -LiteralPath (Join-Path $Game 'RK.exe') -Destination (Join-Path $fixture 'RK.exe')
$launcher = Join-Path $OutputDirectory 'RisingKingdomsLauncher.exe'
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
if (!(Test-Path -LiteralPath (Join-Path $fixture 'Local\English\game.loc.xml'))) { throw 'Native resource translation missing' }
foreach ($relative in $relativeFiles) {
    if ((Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash -eq $hashes[$relative]) { throw "Patch did not change $relative" }
}
Invoke-LauncherTest 'restore' @('--restore',$fixture)
if (Test-Path -LiteralPath (Join-Path $fixture 'Local\English\game.loc.xml')) { throw 'Native resource translation was not removed' }
if ((Get-FileHash -LiteralPath (Join-Path $Game 'RK.exe') -Algorithm SHA256).Hash -ne $exeHash -or (Get-FileHash -LiteralPath (Join-Path $fixture 'RK.exe') -Algorithm SHA256).Hash -ne $exeHash) { throw 'Game executable changed' }
foreach ($relative in $relativeFiles) {
    if ((Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash -ne $hashes[$relative]) { throw "Restore failed: $relative" }
    if ((Get-FileHash -LiteralPath (Join-Path $Game $relative)).Hash -ne $installationHashes[$relative]) { throw "Original installation changed: $relative" }
}
Write-Host "PASS: PAK/campaign/font/native translation built, twelve-file install and exact restore. Executable and original installation unchanged. Results: $testRoot"
