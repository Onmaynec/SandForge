$ErrorActionPreference = 'Stop'

$OldVersion = '0.5.0' + '-alpha'
$NewVersion = '0.5.0'
$Extensions = @('.cs', '.props', '.ps1', '.md', '.json', '.yml', '.yaml')

Get-ChildItem -Path . -File -Recurse |
    Where-Object {
        $Extensions -contains $_.Extension.ToLowerInvariant() -and
        $_.FullName -notmatch '[\\/](bin|obj|artifacts|\.git)[\\/]'
    } |
    ForEach-Object {
        $Text = Get-Content -LiteralPath $_.FullName -Raw
        if ($Text.Contains($OldVersion)) {
            Set-Content -LiteralPath $_.FullName -Value $Text.Replace($OldVersion, $NewVersion) -Encoding utf8 -NoNewline
        }
    }

$Changelog = Get-Content -LiteralPath 'CHANGELOG.md' -Raw
if (-not $Changelog.Contains('## [0.5.0]')) {
    $Section = @'

## [0.5.0] - 2026-08-02

### Добавлено
- каталог публичных контрактов и версий схем;
- команды `sandforge schema list|describe|validate`;
- versioned JSON-отчёты и package manifest с SHA-256;
- contract tests и compatibility policy.

### Изменено
- версия повышена до `0.5.0`;
- неподдерживаемые версии схем блокируются до выполнения.
'@
    $Marker = '# Changelog'
    $Rest = if ($Changelog.StartsWith($Marker)) {
        $Changelog.Substring($Marker.Length).TrimStart([char[]]"`r`n")
    } else {
        $Changelog
    }
    Set-Content -LiteralPath 'CHANGELOG.md' -Value ($Marker + $Section + "`r`n`r`n" + $Rest) -Encoding utf8 -NoNewline
}

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add -A
if (-not (git diff --cached --quiet)) {
    git commit -m 'Выпустить SandForge 0.5.0'
    git push origin HEAD:main
}
$ReleaseSha = (git rev-parse HEAD).Trim()

Write-Host 'Running stable release tests...'
dotnet test SandForge.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

Write-Host 'Building win-x64 package...'
./scripts/package.ps1 -Version $NewVersion

$NotesPath = Join-Path $PWD 'release-notes-v0.5.0.md'
@'
## SandForge 0.5.0

### Основные изменения
- проверка совместимости через `sandforge schema`;
- JSON Schema для основных форматов;
- versioned JSON-отчёты;
- package manifest с SHA-256;
- RU/EN интерфейс и contract tests.

Сравните SHA-256 архива с приложенным файлом `.sha256`.
'@ | Set-Content -LiteralPath $NotesPath -Encoding utf8

$Zip = 'artifacts/SandForge-0.5.0-win-x64.zip'
$Checksum = 'artifacts/SandForge-0.5.0-win-x64.zip.sha256'
$Existing = $false
try {
    gh release view v0.5.0 --repo Onmaynec/SandForge *> $null
    $Existing = $LASTEXITCODE -eq 0
} catch {
    $Existing = $false
}

if ($Existing) {
    gh release upload v0.5.0 $Zip $Checksum --repo Onmaynec/SandForge --clobber
} else {
    gh release create v0.5.0 $Zip $Checksum --repo Onmaynec/SandForge --target $ReleaseSha --title 'SandForge v0.5.0' --notes-file $NotesPath
}
if ($LASTEXITCODE -ne 0) { throw 'GitHub Release publication failed.' }

gh release edit v0.5.0 --repo Onmaynec/SandForge --title 'SandForge v0.5.0' --notes-file $NotesPath --draft=false --prerelease=false --latest
if ($LASTEXITCODE -ne 0) { throw 'GitHub Release metadata update failed.' }

$Release = gh release view v0.5.0 --repo Onmaynec/SandForge --json tagName,isDraft,isPrerelease,url,assets | ConvertFrom-Json
$AssetNames = @($Release.assets | ForEach-Object name)
if ($Release.tagName -ne 'v0.5.0' -or $Release.isDraft -or $Release.isPrerelease) {
    throw 'Release status verification failed.'
}
if ($AssetNames -notcontains 'SandForge-0.5.0-win-x64.zip') {
    throw 'ZIP asset is missing.'
}
if ($AssetNames -notcontains 'SandForge-0.5.0-win-x64.zip.sha256') {
    throw 'SHA-256 asset is missing.'
}

Write-Host "Published: $($Release.url)"
