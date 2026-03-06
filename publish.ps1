$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "dotnet publish -c Release -r win-x64 --self-contained false -o publish/"
dotnet publish -c Release -r win-x64 --self-contained false -o publish/
exit $LASTEXITCODE
