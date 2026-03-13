# IntLimiter Release Build Script
# Requirements: .NET 8 SDK, Velopack CLI (vpk)

$ErrorActionPreference = "Stop"

Write-Host "Installing/Updating Velopack CLI..."
dotnet tool update -g vpk

Write-Host "Publishing IntLimiter.App..."
$publishDir = Join-Path $PSScriptRoot "src\IntLimiter.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\"
$projectPath = Join-Path $PSScriptRoot "src\IntLimiter.App\IntLimiter.App.csproj"

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

dotnet publish $projectPath -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishDir="$publishDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
    exit 1
}

Write-Host "Packaging with Velopack..."

if (-Not (Test-Path "$publishDir\IntLimiter.App.exe")) {
    Write-Error "IntLimiter.App.exe not found in $publishDir!"
    exit 1
}

if (Test-Path "Releases") { Remove-Item -Recurse -Force "Releases" }

vpk pack -u IntLimiter -v 1.0.0 -p $publishDir -e IntLimiter.App.exe -o Releases

if ($LASTEXITCODE -ne 0) {
    Write-Error "Velopack packaging failed!"
    exit 1
}

Write-Host "Release packaged successfully in /Releases folder!"
