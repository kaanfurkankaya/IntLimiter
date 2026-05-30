param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bu script Administrator PowerShell ile calistirilmalidir."
    }
}

Assert-Admin

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$serviceProject = Join-Path $repoRoot "src\IntLimiter.Service\IntLimiter.Service.csproj"
$publishDir = Join-Path $repoRoot "src\IntLimiter.Service\bin\$Configuration\net8.0-windows\win-x64\publish"
$serviceExe = Join-Path $publishDir "IntLimiter.Service.exe"

dotnet publish $serviceProject -c $Configuration -r win-x64 --self-contained false

if (-not (Test-Path $serviceExe)) {
    throw "Service executable bulunamadi: $serviceExe"
}

$windivertSource = Join-Path $repoRoot "third_party\WinDivert\x64"
if (Test-Path $windivertSource) {
    Copy-Item (Join-Path $windivertSource "WinDivert.dll") $publishDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $windivertSource "WinDivert64.sys") $publishDir -ErrorAction SilentlyContinue
}

if (Get-Service -Name "IntLimiter.Service" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "IntLimiter.Service" -ErrorAction SilentlyContinue
    sc.exe delete "IntLimiter.Service" | Out-Null
    Start-Sleep -Seconds 2
}

New-Service `
    -Name "IntLimiter.Service" `
    -DisplayName "IntLimiter Service" `
    -Description "IntLimiter traffic shaping service" `
    -BinaryPathName "`"$serviceExe`"" `
    -StartupType Automatic | Out-Null

Start-Service -Name "IntLimiter.Service"
Get-Service -Name "IntLimiter.Service"


