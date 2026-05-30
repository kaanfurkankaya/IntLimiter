param(
    [int]$LimitKb = 512,
    [string]$Url = "https://speed.cloudflare.com/__down?bytes=10000000"
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Start-Process -FilePath "powershell.exe" `
        -Verb RunAs `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-LimitKb", $LimitKb, "-Url", "`"$Url`"")
    exit
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resultDir = Join-Path $repoRoot "logs\speed-tests"
New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportPath = Join-Path $resultDir "$stamp-global-download-proof.txt"
$jsonPath = Join-Path $resultDir "$stamp-global-download-proof.json"
$serviceProject = Join-Path $repoRoot "src\IntLimiter.Service\IntLimiter.Service.csproj"
$speedScript = Join-Path $PSScriptRoot "run-speed-test.ps1"
$verifyScript = Join-Path $PSScriptRoot "verify-intlimiter-runtime.ps1"
$serviceProcess = $null

function Add-Report {
    param([string]$Line)
    $Line | Tee-Object -FilePath $reportPath -Append
}

function Invoke-Pipe {
    param([string]$Command, [object]$Payload = $null)

    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", "IntLimiter.Service", [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(5000)
    $writer = [IO.StreamWriter]::new($pipe, [Text.Encoding]::UTF8, 65536, $true)
    $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 65536, $true)
    $writer.AutoFlush = $true
    $request = @{ command = $Command; payload = $Payload } | ConvertTo-Json -Depth 10 -Compress
    $writer.WriteLine($request)
    $line = $reader.ReadLine()
    $reader.Dispose()
    $writer.Dispose()
    $pipe.Dispose()
    if (-not $line) { throw "Empty IPC response for $Command" }
    $response = $line | ConvertFrom-Json
    if (-not $response.success) { throw "IPC $Command failed: $($response.error)" }
    return $response.data
}

function Wait-Pipe {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        try {
            Invoke-Pipe -Command "GetDiagnostics" | Out-Null
            return
        } catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)
    throw "IntLimiter service pipe did not become ready."
}

function Get-LatestSpeedResult {
    param([string]$Mode)
    $file = Get-ChildItem -LiteralPath $resultDir -Filter "*-download-$($Mode.ToLowerInvariant()).json" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $file) { throw "Speed result not found for $Mode" }
    return Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
}

try {
    Add-Report "IntLimiter global download proof test"
    Add-Report "Timestamp: $(Get-Date -Format o)"
    Add-Report "URL: $Url"
    Add-Report "Limit: $LimitKb KB/s"
    Add-Report ""

    $dll = Join-Path $repoRoot "src\IntLimiter.Service\bin\Debug\net8.0-windows\WinDivert.dll"
    $sys = Join-Path $repoRoot "src\IntLimiter.Service\bin\Debug\net8.0-windows\WinDivert64.sys"
    if (-not (Test-Path $dll) -or -not (Test-Path $sys)) {
        throw "WinDivert files missing in service Debug output. Run prepare-windivert.ps1 first."
    }

    try {
        Invoke-Pipe -Command "GetDiagnostics" | Out-Null
        Add-Report "Service pipe: already running"
    } catch {
        Add-Report "Service pipe: starting dev service"
        $serviceProcess = Start-Process -FilePath "dotnet" `
            -ArgumentList @("run", "--project", $serviceProject, "--no-build") `
            -WorkingDirectory $repoRoot `
            -PassThru `
            -WindowStyle Hidden
        Wait-Pipe
    }

    Add-Report ""
    Add-Report "Running BEFORE speed test..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $speedScript -Direction Download -Mode Before -Url $Url
    if ($LASTEXITCODE -ne 0) { throw "Before speed test failed: $LASTEXITCODE" }
    $before = Get-LatestSpeedResult -Mode Before
    Add-Report ("Before average: {0:N2} KB/s" -f ([double]$before.averageBytesPerSecond / 1KB))

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $rule = @{
        ruleId = [guid]::NewGuid().ToString()
        name = "Proof global download $LimitKb KB/s"
        scope = "Global"
        direction = "Download"
        limitBytesPerSecond = $LimitKb * 1024
        enabled = $true
        createdAt = $now
        updatedAt = $now
    }

    Add-Report "Applying global download rule..."
    Invoke-Pipe -Command "ApplyRules" -Payload @($rule) | Out-Null
    Start-Sleep -Seconds 2
    $diagAfterApply = Invoke-Pipe -Command "GetDiagnostics"
    Add-Report "Runtime after apply: mode=$($diagAfterApply.runtimeMode), running=$($diagAfterApply.isRunning), message=$($diagAfterApply.message)"

    Add-Report ""
    Add-Report "Running AFTER speed test..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $speedScript -Direction Download -Mode After -Url $Url
    if ($LASTEXITCODE -ne 0) { throw "After speed test failed: $LASTEXITCODE" }
    $after = Get-LatestSpeedResult -Mode After
    Add-Report ("After average : {0:N2} KB/s" -f ([double]$after.averageBytesPerSecond / 1KB))

    $diag = Invoke-Pipe -Command "GetDiagnostics"
    $beforeBps = [double]$before.averageBytesPerSecond
    $afterBps = [double]$after.averageBytesPerSecond
    $ratio = if ($beforeBps -gt 0) { $afterBps / $beforeBps } else { 1.0 }

    Add-Report ""
    Add-Report "Diagnostics:"
    Add-Report "Runtime mode: $($diag.runtimeMode)"
    Add-Report "Captured packets: $($diag.capturedPackets)"
    Add-Report "Delayed packets: $($diag.delayedPackets)"
    Add-Report "Reinjected packets: $($diag.reinjectedPackets)"
    Add-Report "Dropped packets: $($diag.droppedPackets)"
    Add-Report "Process mapping success: $($diag.processMappingSuccess)"
    Add-Report "Process mapping failed: $($diag.processMappingFailed)"
    Add-Report ("After/Before: {0:P1}" -f $ratio)

    $overall = if ($diag.runtimeMode -ne "WinDivert") {
        "FAILED - WinDivert mode is not active"
    } elseif ([int64]$diag.capturedPackets -le 0) {
        "FAILED - No packets captured"
    } elseif ([int64]$diag.delayedPackets -le 0) {
        "FAILED - No delayed packets"
    } elseif ([int64]$diag.reinjectedPackets -le 0) {
        "FAILED - No reinjected packets"
    } elseif ($ratio -lt 0.85) {
        "SUCCESS - speed dropped and packet counters prove shaping path"
    } else {
        "SUSPICIOUS - packet counters exist but measured speed did not drop enough"
    }

    Add-Report "Overall: $overall"

    @{
        timestamp = (Get-Date).ToString("o")
        url = $Url
        limitKb = $LimitKb
        beforeAverageBytesPerSecond = $beforeBps
        afterAverageBytesPerSecond = $afterBps
        ratio = $ratio
        diagnostics = $diag
        overall = $overall
        reportPath = $reportPath
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

    Add-Report "JSON: $jsonPath"
}
catch {
    Add-Report "ERROR: $($_.Exception.Message)"
    throw
}
finally {
    try {
        Invoke-Pipe -Command "StopAll" | Out-Null
        Add-Report "StopAll sent."
    } catch {
        Add-Report "StopAll failed or service pipe unavailable: $($_.Exception.Message)"
    }

    if ($serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force -ErrorAction SilentlyContinue
        Add-Report "Temporary dev service stopped."
    }

    Add-Report "Report: $reportPath"
}


