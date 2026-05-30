param(
    [string]$Configuration = "Release",
    [switch]$CreateDesktopShortcut
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$distDir = Join-Path $repoRoot "dist\IntLimiter"
$clientProject = Join-Path $repoRoot "src\IntLimiter.Client\IntLimiter.Client.csproj"
$serviceProject = Join-Path $repoRoot "src\IntLimiter.Service\IntLimiter.Service.csproj"
$windivertSource = Join-Path $repoRoot "third_party\WinDivert\x64"

function Stop-RunningDistProcesses {
    param([string]$Directory)

    $fullDirectory = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    $processes = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        ($_.Name -in @("IntLimiter.exe", "IntLimiter.Service.exe")) -and
        ($_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($fullDirectory, [StringComparison]::OrdinalIgnoreCase))
    })

    foreach ($process in $processes) {
        Write-Host "Stopping running copy: $($process.Name) ($($process.ProcessId))"
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    if ($processes.Count -gt 0) {
        Start-Sleep -Milliseconds 800
    }
}

Push-Location $repoRoot
try {
    if (Test-Path $distDir) {
        Stop-RunningDistProcesses -Directory $distDir
        try {
            Remove-Item -LiteralPath $distDir -Recurse -Force
        } catch {
            $fallbackName = "IntLimiter-" + (Get-Date -Format "yyyyMMdd-HHmmss")
            $distDir = Join-Path $repoRoot "dist\$fallbackName"
            Write-Warning "Existing dist folder is locked. Publishing to: $distDir"
        }
    }

    New-Item -ItemType Directory -Path $distDir | Out-Null

    Write-Host "Publishing IntLimiter service..."
    dotnet publish $serviceProject -c $Configuration -r win-x64 --self-contained false -o $distDir

    Write-Host "Publishing IntLimiter client..."
    dotnet publish $clientProject -c $Configuration -r win-x64 --self-contained false -o $distDir

    if (Test-Path $windivertSource) {
        $dll = Join-Path $windivertSource "WinDivert.dll"
        $sys = Join-Path $windivertSource "WinDivert64.sys"
        if ((Test-Path $dll) -and (Test-Path $sys)) {
            Copy-Item -LiteralPath $dll -Destination $distDir -Force
            Copy-Item -LiteralPath $sys -Destination $distDir -Force
            Write-Host "WinDivert files copied."
        } else {
            Write-Warning "WinDivert.dll or WinDivert64.sys is missing under: $windivertSource"
            Write-Warning "The app will open, but real packet shaping is not verified without these files."
        }
    } else {
        Write-Warning "WinDivert source folder is missing: $windivertSource"
    }

    $clientExe = Join-Path $distDir "IntLimiter.exe"
    $serviceExe = Join-Path $distDir "IntLimiter.Service.exe"
    if (-not (Test-Path $clientExe)) {
        throw "Client executable was not produced: $clientExe"
    }

    if (-not (Test-Path $serviceExe)) {
        throw "Service executable was not produced: $serviceExe"
    }

    if ($CreateDesktopShortcut) {
        $desktop = [Environment]::GetFolderPath("Desktop")
        $shortcutPath = Join-Path $desktop "IntLimiter.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $clientExe
        $shortcut.WorkingDirectory = $distDir
        $shortcut.Description = "IntLimiter"
        $shortcut.IconLocation = "$clientExe,0"
        $shortcut.Save()
        Write-Host "Desktop shortcut created: $shortcutPath"
    }

    Write-Host ""
    Write-Host "Ready:"
    Write-Host "  $clientExe"
    Write-Host ""
    Write-Host "Double-click IntLimiter.exe to open the app. The client is configured to request Administrator permission."
    Write-Host "For normal installation, prefer scripts\build-installer.ps1 and run dist\installer\IntLimiterSetup.exe."
}
finally {
    Pop-Location
}
