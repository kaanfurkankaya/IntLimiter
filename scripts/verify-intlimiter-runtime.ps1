$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$programDataDir = Join-Path $env:ProgramData "IntLimiter"
$rulePath = Join-Path $programDataDir "rules.json"
$logPath = Join-Path $programDataDir "IntLimiter.log.jsonl"
$serviceDebugDir = Join-Path $repoRoot "src\IntLimiter.Service\bin\Debug\net8.0-windows"
$serviceReleaseDir = Join-Path $repoRoot "src\IntLimiter.Service\bin\Release\net8.0-windows"
$clientDebugExe = Join-Path $repoRoot "src\IntLimiter.Client\bin\Debug\net8.0-windows\IntLimiter.exe"
$serviceDebugExe = Join-Path $serviceDebugDir "IntLimiter.Service.exe"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-LogEvents {
    if (-not (Test-Path $logPath)) { return @() }
    Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            try { $_ | ConvertFrom-Json } catch { $null }
        } |
        Where-Object { $_ -ne $null }
}

function Count-Event {
    param([object[]]$Events, [string]$EventName, [string]$MessageNeedle)
    @($Events | Where-Object {
        $_.event -eq $EventName -or ($_.message -and $_.message.ToString().IndexOf($MessageNeedle, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    }).Count
}

function Invoke-IntLimiterPipe {
    param([string]$Command)
    try {
        $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", "IntLimiter.Service", [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
        $pipe.Connect(1500)
        $writer = [IO.StreamWriter]::new($pipe, [Text.Encoding]::UTF8, 4096, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 4096, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine((@{ command = $Command; payload = $null } | ConvertTo-Json -Compress))
        $line = $reader.ReadLine()
        $reader.Dispose()
        $writer.Dispose()
        $pipe.Dispose()
        if ($line) { return ($line | ConvertFrom-Json) }
    } catch {
        return $null
    }
    return $null
}

$events = @(Get-LogEvents)
$diagnosticsResponse = Invoke-IntLimiterPipe -Command "GetDiagnostics"
$diagnostics = if ($diagnosticsResponse -and $diagnosticsResponse.success) { $diagnosticsResponse.data } else { $null }

$serviceProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq "IntLimiter.Service.exe" -or ($_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*IntLimiter.Service*")
})
$clientProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq "IntLimiter.exe" -or $_.Name -eq "IntLimiter.Client.exe" -or ($_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*IntLimiter.Client*")
})

$windivertDebug = (Test-Path (Join-Path $serviceDebugDir "WinDivert.dll")) -and (Test-Path (Join-Path $serviceDebugDir "WinDivert64.sys"))
$windivertRelease = (Test-Path (Join-Path $serviceReleaseDir "WinDivert.dll")) -and (Test-Path (Join-Path $serviceReleaseDir "WinDivert64.sys"))
$windivertOk = $windivertDebug -or $windivertRelease

$captured = if ($diagnostics) { [int64]$diagnostics.capturedPackets } else { Count-Event $events "PacketCaptured" "packet captured" }
$delayed = if ($diagnostics) { [int64]$diagnostics.delayedPackets } else { Count-Event $events "PacketDelayed" "packet delayed" }
$reinjected = if ($diagnostics) { [int64]$diagnostics.reinjectedPackets } else { Count-Event $events "PacketReinjected" "packet reinjected" }
$dropped = if ($diagnostics) { [int64]$diagnostics.droppedPackets } else { Count-Event $events "PacketDropped" "packet dropped" }
$mappingOk = if ($diagnostics) { [int64]$diagnostics.processMappingSuccess } else { Count-Event $events "ProcessMappingSuccess" "process mapping success" }
$mappingFail = if ($diagnostics) { [int64]$diagnostics.processMappingFailed } else { Count-Event $events "ProcessMappingFailed" "process mapping failed" }
$queue = if ($diagnostics) { [int64]$diagnostics.queueLength } else { 0 }

$runtimeMode = if ($diagnostics) {
    if ($diagnostics.runtimeMode -eq "WinDivert" -and $diagnostics.isRunning) { "WinDivert Active" }
    elseif ($diagnostics.runtimeMode -eq "Monitoring" -and $diagnostics.isRunning) { "WinDivert Monitoring" }
    elseif ($diagnostics.runtimeMode -eq "QosPolicyFallback" -and $diagnostics.isRunning) { "QoS Fallback Active" }
    else { "$($diagnostics.runtimeMode)" }
} elseif ((Count-Event $events "WinDivertModeActive" "WinDivert mode active") -gt 0) {
    "WinDivert Active (from logs)"
} elseif ((Count-Event $events "WinDivertMonitoringActive" "WinDivert passive monitoring active") -gt 0) {
    "WinDivert Monitoring (from logs)"
} elseif ((Count-Event $events "QosFallbackModeActive" "QoS fallback mode active") -gt 0) {
    "QoS Fallback Active (from logs)"
} else {
    "Unknown"
}

$overall = "NOT READY"
if (-not $windivertOk) {
    $overall = "DOWNLOAD LIMITING NOT VERIFIED - WinDivert files missing"
} elseif ($runtimeMode -like "QoS*") {
    $overall = "ONLY OUTBOUND/UPLOAD FALLBACK IS AVAILABLE"
} elseif ($runtimeMode -eq "Error") {
    $overall = "RUNTIME ERROR - CHECK LOGS"
} elseif ($runtimeMode -like "WinDivert Monitoring*" -and $captured -gt 0) {
    $overall = "MONITORING READY - ADD A LIMIT TO VERIFY SHAPING"
} elseif ($runtimeMode -like "WinDivert Monitoring*") {
    $overall = "MONITORING ACTIVE BUT NO PACKETS CAPTURED YET"
} elseif ($captured -eq 0) {
    $overall = "NO PACKETS CAPTURED - LIMITER IS NOT PROVEN TO WORK"
} elseif ($delayed -eq 0) {
    $overall = "PACKETS CAPTURED BUT NO DELAYED PACKETS - LIMIT RULE MAY NOT BE ACTIVE"
} elseif ($reinjected -gt 0) {
    $overall = "READY FOR REAL TEST"
}

$rows = [ordered]@{
    "Admin" = if (Test-Admin) { "OK" } else { "NOT ADMIN" }
    "Build" = if ((Test-Path $serviceDebugExe) -and (Test-Path $clientDebugExe)) { "OK" } else { "MISSING - run dotnet build" }
    "Service" = if ($serviceProcesses.Count -gt 0) { "Running" } else { "Not running" }
    "Client" = if ($clientProcesses.Count -gt 0) { "Running" } else { "Not running" }
    "WinDivert files" = if ($windivertOk) { "OK" } else { "MISSING" }
    "Rules file" = if (Test-Path $rulePath) { $rulePath } else { "Missing" }
    "Log file" = if (Test-Path $logPath) { $logPath } else { "Missing" }
    "Runtime mode" = $runtimeMode
    "Captured packets" = $captured
    "Delayed packets" = $delayed
    "Reinjected packets" = $reinjected
    "Dropped packets" = $dropped
    "Process mapping success" = $mappingOk
    "Process mapping failed" = $mappingFail
    "Queue length" = $queue
    "WinDivert opened events" = Count-Event $events "WinDivertOpened" "WinDivert opened"
    "WinDivert missing events" = Count-Event $events "WinDivertFilesMissing" "WinDivert files missing"
    "Stop all limits events" = Count-Event $events "StopAllLimitsExecuted" "Stop all limits"
    "Overall" = $overall
}

$maxKey = ($rows.Keys | Measure-Object -Maximum Length).Maximum
foreach ($key in $rows.Keys) {
    "{0}: {1}" -f $key.PadRight($maxKey), $rows[$key]
}

if ($overall -ne "READY FOR REAL TEST") {
    Write-Host ""
    Write-Warning $overall
    Write-Host "If traffic shaping is not effective, inspect:"
    Write-Host "  $logPath"
}


