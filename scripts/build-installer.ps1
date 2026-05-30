param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$payloadDir = Join-Path $repoRoot "artifacts\installer\payload"
$installerOut = Join-Path $repoRoot "dist\installer"
$payloadZip = Join-Path $repoRoot "src\IntLimiter.Setup\Payload\IntLimiterPayload.zip"
$clientProject = Join-Path $repoRoot "src\IntLimiter.Client\IntLimiter.Client.csproj"
$serviceProject = Join-Path $repoRoot "src\IntLimiter.Service\IntLimiter.Service.csproj"
$setupProject = Join-Path $repoRoot "src\IntLimiter.Setup\IntLimiter.Setup.csproj"
$windivertSource = Join-Path $repoRoot "third_party\WinDivert\x64"

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw $Message
    }
}

Push-Location $repoRoot
try {
    Write-Host "Building IntLimiter installer payload..." -ForegroundColor Cyan

    Remove-Item -LiteralPath $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $installerOut -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $payloadZip -Force -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
    New-Item -ItemType Directory -Force -Path $installerOut | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $payloadZip) | Out-Null

    $windivertDll = Join-Path $windivertSource "WinDivert.dll"
    $windivertSys = Join-Path $windivertSource "WinDivert64.sys"
    Assert-FileExists -Path $windivertDll -Message "WinDivert.dll missing. Put it under third_party\WinDivert\x64 before building the installer."
    Assert-FileExists -Path $windivertSys -Message "WinDivert64.sys missing. Put it under third_party\WinDivert\x64 before building the installer."

    Write-Host "Publishing service..."
    dotnet publish $serviceProject -c $Configuration -r win-x64 --self-contained true -o $payloadDir

    Write-Host "Publishing client..."
    dotnet publish $clientProject -c $Configuration -r win-x64 --self-contained true -o $payloadDir

    Copy-Item -LiteralPath $windivertDll -Destination $payloadDir -Force
    Copy-Item -LiteralPath $windivertSys -Destination $payloadDir -Force

    Assert-FileExists -Path (Join-Path $payloadDir "IntLimiter.exe") -Message "Client executable was not produced."
    Assert-FileExists -Path (Join-Path $payloadDir "IntLimiter.Service.exe") -Message "Service executable was not produced."
    Assert-FileExists -Path (Join-Path $payloadDir "WinDivert.dll") -Message "WinDivert.dll was not copied into payload."
    Assert-FileExists -Path (Join-Path $payloadDir "WinDivert64.sys") -Message "WinDivert64.sys was not copied into payload."

    Write-Host "Packing payload..."
    Compress-Archive -Path (Join-Path $payloadDir "*") -DestinationPath $payloadZip -Force

    Write-Host "Publishing setup executable..."
    dotnet publish $setupProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $installerOut

    $installerExe = Join-Path $installerOut "IntLimiterSetup.exe"
    Assert-FileExists -Path $installerExe -Message "Installer executable was not produced."

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $installerExe
    $hashPath = Join-Path $installerOut "IntLimiterSetup.sha256.txt"
    "$($hash.Hash)  IntLimiterSetup.exe" | Set-Content -LiteralPath $hashPath -Encoding UTF8

    Write-Host ""
    Write-Host "Installer ready:" -ForegroundColor Green
    Write-Host "  $installerExe"
    Write-Host "SHA256:"
    Write-Host "  $($hash.Hash)"
    Write-Host ""
    Write-Host "Run IntLimiterSetup.exe once. It will ask for Administrator permission, install the service, and create shortcuts."
}
finally {
    Pop-Location
}
