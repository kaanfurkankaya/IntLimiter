# IntLimiter Release Build Script
# Requirements: .NET 8 SDK, Velopack CLI (vpk)

$ErrorActionPreference = "Stop"

Write-Host "Installing/Updating Velopack CLI..."
dotnet tool update -g vpk

Write-Host "Publishing IntLimiter.App..."
dotnet publish src\IntLimiter.App\IntLimiter.App.csproj -c Release -p:Platform=x64 --self-contained true -r win-x64

Write-Host "Packaging with Velopack..."
$publishDir = "src\IntLimiter.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

if (Test-Path "Releases") { Remove-Item -Recurse -Force "Releases" }

vpk pack -u IntLimiter -v 1.0.0 -p $publishDir -e IntLimiter.App.exe -o Releases

Write-Host "Release packaged successfully in /Releases folder!"
