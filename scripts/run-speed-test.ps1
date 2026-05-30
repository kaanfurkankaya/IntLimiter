param(
    [ValidateSet("Download", "Upload")]
    [string]$Direction = "Download",

    [ValidateSet("Before", "After")]
    [string]$Mode = "Before",

    [string]$Url = "https://speed.cloudflare.com/__down?bytes=10000000",

    [string]$OutputFile
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resultDir = Join-Path $repoRoot "logs\speed-tests"
New-Item -ItemType Directory -Force -Path $resultDir | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    $OutputFile = Join-Path $env:TEMP "intlimiter-$($Direction.ToLowerInvariant())-$($Mode.ToLowerInvariant())-$timestamp.bin"
}

function Convert-BytesPerSecond {
    param([double]$BytesPerSecond)
    if ($BytesPerSecond -ge 1MB) { return "{0:N2} MB/s" -f ($BytesPerSecond / 1MB) }
    if ($BytesPerSecond -ge 1KB) { return "{0:N2} KB/s" -f ($BytesPerSecond / 1KB) }
    return "{0:N2} B/s" -f $BytesPerSecond
}

function Write-Result {
    param([hashtable]$Result)
    $jsonPath = Join-Path $resultDir "$timestamp-$($Direction.ToLowerInvariant())-$($Mode.ToLowerInvariant()).json"
    $txtPath = Join-Path $resultDir "$timestamp-$($Direction.ToLowerInvariant())-$($Mode.ToLowerInvariant()).txt"
    $Result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    $Result.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name): $($_.Value)" } | Set-Content -LiteralPath $txtPath -Encoding UTF8
    Write-Host "Result JSON: $jsonPath"
    Write-Host "Result TXT : $txtPath"
}

Write-Host "IntLimiter speed test"
Write-Host "  Direction : $Direction"
Write-Host "  Mode      : $Mode"
Write-Host "  URL/Host  : $Url"
Write-Host "  Output    : $OutputFile"

if ($Direction -eq "Download") {
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe not found."
    }

    Remove-Item -LiteralPath $OutputFile -Force -ErrorAction SilentlyContinue
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & curl.exe -L --fail --output $OutputFile $Url
    $curlExit = $LASTEXITCODE
    $sw.Stop()

    if ($curlExit -ne 0) {
        throw "curl.exe failed with exit code $curlExit"
    }

    $bytes = (Get-Item -LiteralPath $OutputFile).Length
    $seconds = [Math]::Max(0.001, $sw.Elapsed.TotalSeconds)
    $bps = $bytes / $seconds
    $result = @{
        timestamp = (Get-Date).ToString("o")
        direction = $Direction
        mode = $Mode
        url = $Url
        outputFile = $OutputFile
        bytes = $bytes
        durationSeconds = [Math]::Round($seconds, 3)
        averageBytesPerSecond = [Math]::Round($bps, 2)
        averageHuman = Convert-BytesPerSecond $bps
        status = "Completed"
    }

    Write-Host ("Duration : {0:N2}s" -f $seconds)
    Write-Host ("Size     : {0:N0} bytes" -f $bytes)
    Write-Host ("Average  : {0}" -f (Convert-BytesPerSecond $bps))
    Write-Result $result
    exit 0
}

Write-Warning "Upload testing needs a real upload endpoint or an iperf3 server. Download limiting cannot be proven by this mode."

if ($Url -and $Url -match "^https?://") {
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe not found."
    }

    $payload = Join-Path $env:TEMP "intlimiter-upload-payload-10mb.bin"
    if (-not (Test-Path $payload)) {
        $bytes = New-Object byte[] (10MB)
        [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        [IO.File]::WriteAllBytes($payload, $bytes)
    }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    & curl.exe -L --fail -X PUT --data-binary "@$payload" $Url
    $curlExit = $LASTEXITCODE
    $sw.Stop()

    $size = (Get-Item -LiteralPath $payload).Length
    $seconds = [Math]::Max(0.001, $sw.Elapsed.TotalSeconds)
    $bps = $size / $seconds
    $status = if ($curlExit -eq 0) { "Completed" } else { "Failed" }
    $result = @{
        timestamp = (Get-Date).ToString("o")
        direction = $Direction
        mode = $Mode
        url = $Url
        payloadFile = $payload
        bytes = $size
        durationSeconds = [Math]::Round($seconds, 3)
        averageBytesPerSecond = [Math]::Round($bps, 2)
        averageHuman = Convert-BytesPerSecond $bps
        status = $status
        note = "HTTP PUT upload test; endpoint must accept PUT bodies."
    }
    Write-Result $result
    if ($curlExit -ne 0) { exit $curlExit }
    exit 0
}

if (Get-Command iperf3.exe -ErrorAction SilentlyContinue) {
    $result = @{
        timestamp = (Get-Date).ToString("o")
        direction = $Direction
        mode = $Mode
        status = "NotRun"
        note = "iperf3.exe is installed, but no server host was provided. Run: iperf3.exe -c <server> -t 20"
    }
    Write-Warning $result.note
    Write-Result $result
    exit 3
}

$manual = "Upload test not run. Provide an HTTP upload URL, or install/use iperf3 with a reachable server."
Write-Warning $manual
Write-Result @{
    timestamp = (Get-Date).ToString("o")
    direction = $Direction
    mode = $Mode
    status = "NotRun"
    note = $manual
}
exit 3
