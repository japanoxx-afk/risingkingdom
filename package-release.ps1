param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('build\release-' + $version.Version) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Release output already exists: $OutputDirectory. Use a new output directory; published versions must not be replaced." }
$payload = Join-Path $OutputDirectory 'payload'
& (Join-Path $root 'build.ps1') -OutputDirectory $payload
$files = @('RisingKingdomsLauncher.exe','translations/ko.json','version.json')
$zipName = "RisingKingdomsKO-$($version.Version)-win.zip"
$zipPath = Join-Path $OutputDirectory $zipName
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $payload $relative),$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
$manifestFiles = @($files | ForEach-Object {
    $path = Join-Path $payload $_
    @{ Path=$_; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); Size=(Get-Item -LiteralPath $path).Length }
})
$manifest = @{ Schema=1; Version=$version.Version; AssetName=$zipName; Sha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); Size=(Get-Item -LiteralPath $zipPath).Length; Files=$manifestFiles }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'update.json'),($manifest | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$notes = @"
런처 v$($version.Version) / 번역 패치 v$($version.PatchVersion)

ZIP을 풀고 RisingKingdomsLauncher.exe를 실행하세요. 실행할 때 최신 정식 배포를 자동으로 확인합니다.
게임 파일과 글꼴은 이 배포에 포함하지 않으며 패치 적용 시 설치본에서 생성합니다.
번역은 기계번역 초안이고 실제 게임 화면 검수는 아직 완료되지 않았습니다.

자세한 변경 사항은 저장소의 CHANGELOG.md를 확인하세요.
"@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'release-notes.txt'),$notes,[Text.UTF8Encoding]::new($false))
Write-Host "Release package ready: $zipPath"
