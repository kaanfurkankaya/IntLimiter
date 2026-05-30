param(
    [int]$DownloadKb = 0,
    [int]$UploadKb = 0
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    throw "Run this from Administrator PowerShell. The IntLimiter service pipe rejects non-elevated clients."
}

if ($DownloadKb -le 0 -and $UploadKb -le 0) {
    throw "Provide -DownloadKb and/or -UploadKb. Example: .\scripts\apply-global-limit.ps1 -DownloadKb 512"
}

function Invoke-IntLimiterPipe {
    param([string]$Command, [object]$Payload = $null)

    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", "IntLimiter.Service", [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(5000)
    $writer = [IO.StreamWriter]::new($pipe, [Text.Encoding]::UTF8, 65536, $true)
    $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 65536, $true)
    $writer.AutoFlush = $true
    $writer.WriteLine((@{ command = $Command; payload = $Payload } | ConvertTo-Json -Depth 20 -Compress))
    $line = $reader.ReadLine()
    $reader.Dispose()
    $writer.Dispose()
    $pipe.Dispose()

    if (-not $line) { throw "Empty response from IntLimiter service." }
    $response = $line | ConvertFrom-Json
    if (-not $response.success) { throw "IntLimiter service command failed: $($response.error)" }
    return $response.data
}

$rules = @()
$existing = Invoke-IntLimiterPipe -Command "GetRules"
if ($existing) {
    $rules = @($existing | Where-Object { $_.scope -ne "Global" })
}

$now = (Get-Date).ToUniversalTime().ToString("o")
if ($DownloadKb -gt 0) {
    $rules += @{
        ruleId = [guid]::NewGuid().ToString()
        name = "Global download $DownloadKb KB/s"
        scope = "Global"
        direction = "Download"
        limitBytesPerSecond = $DownloadKb * 1024
        enabled = $true
        createdAt = $now
        updatedAt = $now
    }
}

if ($UploadKb -gt 0) {
    $rules += @{
        ruleId = [guid]::NewGuid().ToString()
        name = "Global upload $UploadKb KB/s"
        scope = "Global"
        direction = "Upload"
        limitBytesPerSecond = $UploadKb * 1024
        enabled = $true
        createdAt = $now
        updatedAt = $now
    }
}

Invoke-IntLimiterPipe -Command "ApplyRules" -Payload $rules | Out-Null
$diag = Invoke-IntLimiterPipe -Command "GetDiagnostics"

Write-Host "Applied global limit."
Write-Host "Runtime mode : $($diag.runtimeMode)"
Write-Host "Is running   : $($diag.isRunning)"
Write-Host "Message      : $($diag.message)"
Write-Host "Last error   : $($diag.lastError)"
