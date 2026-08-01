$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    dotnet test SandForge.sln -c Release
} finally { Pop-Location }
