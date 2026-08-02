$ErrorActionPreference = 'Stop'

$Version = '0.5.0'
$Tag = 'v0.5.0'
$Repository = 'Onmaynec/SandForge'
$ReleaseSha = (git rev-parse HEAD).Trim()

Write-Host 'Проверка стабильных исходников...'
$Project = Get-Content -LiteralPath 'Directory.Build.props' -Raw
if (-not $Project.Contains('<Version>0.5.0</Version>')) {
    throw 'Directory.Build.props не содержит стабильную версию 0.5.0.'
}

dotnet test SandForge.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

Write-Host 'Сборка win-x64 архива...'
./scripts/package.ps1 -Version $Version
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }

$Zip = 'artifacts/SandForge-0.5.0-win-x64.zip'
$Checksum = 'artifacts/SandForge-0.5.0-win-x64.zip.sha256'
if (-not (Test-Path -LiteralPath $Zip)) { throw 'ZIP package is missing.' }
if (-not (Test-Path -LiteralPath $Checksum)) { throw 'SHA-256 file is missing.' }

Write-Host 'Обновление стабильного тега...'
git fetch --tags origin
git tag -d $Tag 2>$null
git tag -a $Tag $ReleaseSha -m 'SandForge v0.5.0'
git push --force origin "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw 'Tag push failed.' }

$NotesPath = Join-Path $PWD 'release-notes-v0.5.0.md'
@(
    '## SandForge 0.5.0',
    '',
    '### Основные изменения',
    '- проверка совместимости через `sandforge schema`;',
    '- JSON Schema для основных форматов SandForge;',
    '- versioned JSON-отчёты;',
    '- package manifest с относительными путями, размером и SHA-256;',
    '- RU/EN интерфейс и contract tests;',
    '- блокировка неизвестных и неподдерживаемых версий схем.',
    '',
    '### Проверка архива',
    'Сравните SHA-256 скачанного ZIP с приложенным файлом `.sha256`.'
) | Set-Content -LiteralPath $NotesPath -Encoding utf8

$ReleaseExists = $false
try {
    gh release view $Tag --repo $Repository *> $null
    $ReleaseExists = $LASTEXITCODE -eq 0
} catch {
    $ReleaseExists = $false
}

if ($ReleaseExists) {
    gh release upload $Tag $Zip $Checksum --repo $Repository --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Release asset upload failed.' }
    gh release edit $Tag --repo $Repository --title 'SandForge v0.5.0' --notes-file $NotesPath --draft=false --prerelease=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Release metadata update failed.' }
} else {
    gh release create $Tag $Zip $Checksum --repo $Repository --target $ReleaseSha --title 'SandForge v0.5.0' --notes-file $NotesPath --latest
    if ($LASTEXITCODE -ne 0) { throw 'GitHub Release publication failed.' }
}

$Release = gh release view $Tag --repo $Repository --json tagName,isDraft,isPrerelease,url,assets | ConvertFrom-Json
$AssetNames = @($Release.assets | ForEach-Object name)
if ($Release.tagName -ne $Tag -or $Release.isDraft -or $Release.isPrerelease) {
    throw 'Release status verification failed.'
}
if ($AssetNames -notcontains 'SandForge-0.5.0-win-x64.zip') {
    throw 'ZIP asset is missing.'
}
if ($AssetNames -notcontains 'SandForge-0.5.0-win-x64.zip.sha256') {
    throw 'SHA-256 asset is missing.'
}

Write-Host "Published: $($Release.url)"
