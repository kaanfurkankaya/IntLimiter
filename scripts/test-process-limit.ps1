param(
    [string]$Url = "https://speed.cloudflare.com/__down?bytes=10000000",
    [double]$SuspiciousRatio = 0.85
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$speedScript = Join-Path $PSScriptRoot "run-speed-test.ps1"
$verifyScript = Join-Path $PSScriptRoot "verify-intlimiter-runtime.ps1"
$resultDir = Join-Path $repoRoot "logs\speed-tests"
New-Item -ItemType Directory -Force -Path $resultDir | Out-Null

function Get-LatestSpeedResult {
    param([string]$Mode)
    $file = Get-ChildItem -LiteralPath $resultDir -Filter "*-download-$($Mode.ToLowerInvariant()).json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $file) { throw "Could not find speed result for mode $Mode" }
    return (Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json)
}

function Invoke-SpeedTest {
    param([string]$Mode)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $speedScript -Direction Download -Mode $Mode -Url $Url
    if ($LASTEXITCODE -ne 0) {
        throw "run-speed-test.ps1 failed for mode $Mode with exit code $LASTEXITCODE"
    }
}

Write-Host "Process-based limit test uses curl.exe."
Write-Host "Step 1: running unrestricted curl download baseline."
Invoke-SpeedTest -Mode Before
$before = Get-LatestSpeedResult -Mode Before

Write-Host ""
Write-Host "To make curl.exe visible in the UI, starting a temporary curl download helper."
$helperOut = Join-Path $env:TEMP "intlimiter-curl-selector.bin"
$helper = Start-Process -FilePath "curl.exe" -ArgumentList @("-L", "--output", $helperOut, $Url) -PassThru -WindowStyle Hidden
try {
    Write-Host ""
    Write-Host "In IntLimiter UI:"
    Write-Host "  1. Click 'Processleri yenile'."
    Write-Host "  2. Select curl.exe."
    Write-Host "  3. Set Per-App Download Limit, for example 512 KB/s."
    Write-Host "  4. Click 'Secili process limitini ekle'."
    Write-Host "  5. Click 'Kurallari uygula'."
    Write-Host ""
    Read-Host "Press Enter here after applying the curl.exe process rule"
}
finally {
    if ($helper -and -not $helper.HasExited) {
        Stop-Process -Id $helper.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Step 2: running curl download after process limit."
Invoke-SpeedTest -Mode After
$after = Get-LatestSpeedResult -Mode After

$beforeBps = [double]$before.averageBytesPerSecond
$afterBps = [double]$after.averageBytesPerSecond
$ratio = if ($beforeBps -gt 0) { $afterBps / $beforeBps } else { 1.0 }

Write-Host ""
Write-Host ("Before average: {0:N2} KB/s" -f ($beforeBps / 1KB))
Write-Host ("After average : {0:N2} KB/s" -f ($afterBps / 1KB))
Write-Host ("After/Before  : {0:P1}" -f $ratio)

Write-Host ""
Write-Host "Runtime verification:"
& $verifyScript

$logPath = Join-Path $env:ProgramData "IntLimiter\IntLimiter.log.jsonl"
$events = @()
if (Test-Path $logPath) {
    $events = @(Get-Content -LiteralPath $logPath | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } | Where-Object { $_ })
}
$captured = @($events | Where-Object { $_.event -eq "PacketCaptured" }).Count
$delayed = @($events | Where-Object { $_.event -eq "PacketDelayed" }).Count
$mappingFailed = @($events | Where-Object { $_.event -eq "ProcessMappingFailed" }).Count
$mappingOk = @($events | Where-Object { $_.event -eq "ProcessMappingSuccess" }).Count
$winDivertActive = @($events | Where-Object { $_.event -eq "WinDivertModeActive" }).Count -gt 0

Write-Host ""
if ($ratio -lt $SuspiciousRatio -and $delayed -gt 0 -and $winDivertActive) {
    Write-Host "Result: SUCCESS - curl traffic slowed and PacketDelayed events exist."
} elseif ($captured -eq 0) {
    Write-Warning "Result: FAILED - No packets captured, limiter is not proven to work."
} elseif (-not $winDivertActive) {
    Write-Warning "Result: SUSPICIOUS - WinDivert active event not found. QoS fallback cannot prove download limiting."
} elseif ($delayed -eq 0) {
    Write-Warning "Result: SUSPICIOUS - Packets captured but no PacketDelayed events. Limit rule exists but traffic shaping is not effective."
} elseif ($ratio -ge $SuspiciousRatio) {
    Write-Warning "Result: SUSPICIOUS - Packet delay exists, but measured speed did not drop enough."
} else {
    Write-Warning "Result: SUSPICIOUS - Review diagnostics."
}

Write-Host "Process mapping success events: $mappingOk"
Write-Host "Process mapping failed events : $mappingFailed"
Write-Host "Log path: $logPath"
