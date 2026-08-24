$ErrorActionPreference = "Stop"
dotnet restore
dotnet build -c Release
Write-Host ""
Write-Host "Build OK. Ejecutable en:"
Write-Host "bin\Release\net10.0-windows\SmoothFolder.exe"
