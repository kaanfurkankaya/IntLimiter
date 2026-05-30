$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host "Restarting as Administrator..."
    Start-Process -FilePath "powershell.exe" `
        -Verb RunAs `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    exit
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$serviceProject = Join-Path $repoRoot "src\IntLimiter.Service\IntLimiter.Service.csproj"
$clientProject = Join-Path $repoRoot "src\IntLimiter.Client\IntLimiter.Client.csproj"
$serviceOut = Join-Path $repoRoot "src\IntLimiter.Service\bin\Debug\net8.0-windows"
$logPath = Join-Path $env:ProgramData "IntLimiter\IntLimiter.log.jsonl"

Push-Location $repoRoot
try {
    Write-Host "Building IntLimiter solution..."
    dotnet build .\IntLimiter.sln

    & (Join-Path $PSScriptRoot "prepare-windivert.ps1")
    $dll = Join-Path $serviceOut "WinDivert.dll"
    $sys = Join-Path $serviceOut "WinDivert64.sys"
    if (-not (Test-Path $dll) -or -not (Test-Path $sys)) {
        Write-Warning "WinDivert dosyalari eksik. Gercek download/upload shaping testi yapilamaz."
        Write-Warning "QoS fallback test edilebilir ama download limit kanitlanamaz."
    } else {
        Write-Host "WinDivert files: OK"
    }

    $serviceCommand = @"
`$Host.UI.RawUI.WindowTitle = 'IntLimiter Service'
Set-Location '$repoRoot'
Write-Host 'Starting IntLimiter.Service in dev console mode...'
Write-Host 'ProgramData log: $logPath'
dotnet run --project '$serviceProject' --no-build
`$exitCode = `$LASTEXITCODE
Write-Host ''
Write-Host 'IntLimiter.Service exited. ExitCode='`$exitCode
Write-Host 'Check log: $logPath'
Read-Host 'Press Enter to close this service window'
"@

    $clientCommand = @"
`$Host.UI.RawUI.WindowTitle = 'IntLimiter Client'
Set-Location '$repoRoot'
Write-Host 'Starting IntLimiter.Client...'
Write-Host 'ProgramData log: $logPath'
dotnet run --project '$clientProject' --no-build
`$exitCode = `$LASTEXITCODE
Write-Host ''
Write-Host 'IntLimiter.Client exited. ExitCode='`$exitCode
Read-Host 'Press Enter to close this client window'
"@

    Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $serviceCommand) -WorkingDirectory $repoRoot
    Start-Sleep -Seconds 2
    Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $clientCommand) -WorkingDirectory $repoRoot

    Write-Host ""
    Write-Host "Dev environment launched."
    Write-Host "Service window and Client window are separate. Log path:"
    Write-Host "  $logPath"
}
finally {
    Pop-Location
}


