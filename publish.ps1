$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "Publishing self-contained win-x64..."
dotnet publish src/ModernUOConfigurator.csproj -c Release -r win-x64 --self-contained true -o publish/
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
