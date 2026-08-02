$ErrorActionPreference = 'Stop'

$OldVersion = '0.5.0' + '-alpha'
$NewVersion = '0.5.0'
$Extensions = @('.cs', '.props', '.ps1', '.md', '.json', '.yml', '.yaml')

Get-ChildItem -Path . -File -Recurse |
    Where-Object {
        $Extensions -contains $_.Extension.ToLowerInvariant() -and
        $_.FullName -notmatch '[\\/](bin|obj|artifacts|\.git)[\\/]' -and
        $_.FullName -notmatch '[\\/]\.github[\\/]workflows[\\/]'
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
    if ($LASTEXITCODE -ne 0) { throw 'Stable version push failed.' }
}
$ReleaseSha = (git rev-parse HEAD).Trim()

Write-Host 'Running stable release tests...'
dotnet test SandForge.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

$ReleaseComplete = $false
try {
    $ExistingJson = gh release view v0.5.0 --repo Onmaynec/SandForge --json tagName,isDraft,isPrerelease,assets 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($ExistingJson)) {
        $ExistingRelease = $ExistingJson | ConvertFrom-Json
        $ExistingNames = @($ExistingRelease.assets | ForEach-Object name)
        $ReleaseComplete =
            $ExistingRelease.tagName -eq 'v0.5.0' -and
            -not $ExistingRelease.isDraft -and
            -not $ExistingRelease.isPrerelease -and
            $ExistingNames -contains 'SandForge-0.5.0-win-x64.zip' -and
            $ExistingNames -contains 'SandForge-0.5.0-win-x64.zip.sha256'
    }
} catch {
    $ReleaseComplete = $false
}

if (-not $ReleaseComplete) {
    git fetch --tags origin
    $RemoteTag = git ls-remote --tags origin 'refs/tags/v0.5.0'
    if (-not [string]::IsNullOrWhiteSpace(($RemoteTag | Out-String))) {
        git push origin ':refs/tags/v0.5.0'
        if ($LASTEXITCODE -ne 0) { throw 'Old tag deletion failed.' }
    }
    git tag -d v0.5.0 2>$null
    git tag -a v0.5.0 $ReleaseSha -m 'SandForge v0.5.0'
    git push origin refs/tags/v0.5.0
    if ($LASTEXITCODE -ne 0) { throw 'Tag push failed.' }
}

Write-Host 'Waiting for the tag workflow to publish the release...'
$Deadline = (Get-Date).AddMinutes(8)
$Release = $null
do {
    Start-Sleep -Seconds 5
    try {
        $Json = gh release view v0.5.0 --repo Onmaynec/SandForge --json tagName,isDraft,isPrerelease,url,assets 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($Json)) {
            $Candidate = $Json | ConvertFrom-Json
            $Names = @($Candidate.assets | ForEach-Object name)
            if ($Candidate.tagName -eq 'v0.5.0' -and
                -not $Candidate.isDraft -and
                -not $Candidate.isPrerelease -and
                $Names -contains 'SandForge-0.5.0-win-x64.zip' -and
                $Names -contains 'SandForge-0.5.0-win-x64.zip.sha256') {
                $Release = $Candidate
                break
            }
        }
    } catch {
        $Release = $null
    }
} while ((Get-Date) -lt $Deadline)

if ($null -eq $Release) { throw 'Published release or required assets were not found.' }
Write-Host "Published: $($Release.url)"
