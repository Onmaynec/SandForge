param([string]$Version = '0.5.0')
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root 'artifacts'
$Stage = Join-Path $Out "SandForge-$Version-win-x64"
Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

dotnet publish (Join-Path $Root 'src\SandForge.Cli\SandForge.Cli.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $Stage
Copy-Item (Join-Path $Root 'templates') $Stage -Recurse
Copy-Item (Join-Path $Root 'schemas') $Stage -Recurse
Copy-Item (Join-Path $Root 'sandforge.json') $Stage
Copy-Item (Join-Path $Root 'LICENSE') $Stage
Copy-Item (Join-Path $Root 'THIRD_PARTY_NOTICES.md') $Stage
Copy-Item (Join-Path $Root 'README.md') (Join-Path $Stage 'README.txt')
New-Item -ItemType File -Path (Join-Path $Stage 'portable.mode') -Force | Out-Null

$StagePrefix = [IO.Path]::GetFullPath($Stage).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$ManifestFiles = @(
  Get-ChildItem -Path $Stage -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
      [ordered]@{
        path = $_.FullName.Substring($StagePrefix.Length).Replace('\', '/')
        size = $_.Length
        sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
      }
    }
)
$Manifest = [ordered]@{
  schemaVersion = 1
  product = 'SandForge'
  version = $Version
  runtimeIdentifier = 'win-x64'
  createdAt = (Get-Date).ToUniversalTime().ToString('O')
  files = $ManifestFiles
}
$Manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $Stage 'manifest.json')

$Zip = Join-Path $Out "SandForge-$Version-win-x64.zip"
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$Stage\*" -DestinationPath $Zip
$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash
"$Hash  $(Split-Path $Zip -Leaf)" | Set-Content -Encoding ASCII "$Zip.sha256"
Write-Host "Created $Zip"
