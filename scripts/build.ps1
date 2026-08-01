$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    dotnet restore
    dotnet build SandForge.sln -c Release --no-restore
} finally { Pop-Location }
