param([string]$Version = '0.1.0')
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
Copy-Item (Join-Path $Root 'README_RU.md') (Join-Path $Stage 'README.txt')
New-Item -ItemType File -Path (Join-Path $Stage 'portable.mode') -Force | Out-Null

$Zip = Join-Path $Out "SandForge-$Version-win-x64.zip"
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$Stage\*" -DestinationPath $Zip
$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash
"$Hash  $(Split-Path $Zip -Leaf)" | Set-Content -Encoding ASCII "$Zip.sha256"
Write-Host "Created $Zip"
