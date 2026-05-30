$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourceDir = Join-Path $repoRoot "third_party\WinDivert\x64"
$sourceDll = Join-Path $sourceDir "WinDivert.dll"
$sourceSys = Join-Path $sourceDir "WinDivert64.sys"
$targets = @(
    (Join-Path $repoRoot "src\IntLimiter.Service\bin\Debug\net8.0-windows"),
    (Join-Path $repoRoot "src\IntLimiter.Service\bin\Release\net8.0-windows"),
    (Join-Path $repoRoot "src\IntLimiter.Service\bin\Release\net8.0-windows\win-x64\publish")
)

function Copy-WinDivertFile {
    param(
        [string]$Source,
        [string]$Destination
    )

    try {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
        return "Copied"
    }
    catch [System.IO.IOException] {
        if (Test-Path $Destination) {
            Write-Warning "Target is locked but already exists, leaving it in place: $Destination"
            return "LockedExisting"
        }

        throw
    }
}

Write-Host "Checking WinDivert source folder:"
Write-Host "  $sourceDir"

if (-not (Test-Path $sourceDll) -or -not (Test-Path $sourceSys)) {
    Write-Host ""
    Write-Warning "WinDivert files are missing."
    Write-Host "Download the official WinDivert 2.2 package from:"
    Write-Host "  https://reqrypt.org/windivert.html"
    Write-Host ""
    Write-Host "Then copy these two files:"
    Write-Host "  WinDivert.dll"
    Write-Host "  WinDivert64.sys"
    Write-Host ""
    Write-Host "Into:"
    Write-Host "  $sourceDir"
    Write-Host ""
    Write-Host "After build/publish, run this script again. Missing output folders are created by build/publish."
    return
}

$copied = 0
foreach ($target in $targets) {
    if (-not (Test-Path $target)) {
        Write-Host "Skipping missing target, build/publish will create it later:"
        Write-Host "  $target"
        continue
    }

    Copy-WinDivertFile -Source $sourceDll -Destination (Join-Path $target "WinDivert.dll") | Out-Null
    Copy-WinDivertFile -Source $sourceSys -Destination (Join-Path $target "WinDivert64.sys") | Out-Null
    Write-Host "Copied WinDivert files to $target"
    $copied++
}

if ($copied -gt 0) {
    Write-Host "WinDivert ready."
} else {
    Write-Host "WinDivert source files exist, but no output folders exist yet. Build or publish first, then rerun this script."
}


