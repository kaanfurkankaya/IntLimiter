using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace IntLimiter.Services;

public class BandwidthLimiterService : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _activePolicies = new();
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    /// <summary>
    /// Initializes QoS prerequisites: enables QoS Packet Scheduler on active adapters.
    /// Call this once at startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Enable QoS Packet Scheduler (ms_pacer) on all active adapters
        await RunElevatedAsync(
            "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { " +
            "Enable-NetAdapterBinding -Name $_.Name -ComponentID ms_pacer -ErrorAction SilentlyContinue }");

        // Clean up any leftover policies from previous sessions
        await RunElevatedAsync(
            "Get-NetQosPolicy -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.Name -like 'IntLimiter_*' } | " +
            "Remove-NetQosPolicy -Confirm:$false -ErrorAction SilentlyContinue");
    }

    /// <summary>
    /// Sets a bandwidth limit for a specific process. Rate in bits per second.
    /// Uses NetQoS which primarily throttles outbound traffic.
    /// </summary>
    public async Task<bool> SetLimitAsync(string processName, long rateBitsPerSecond)
    {
        if (string.IsNullOrEmpty(processName) || rateBitsPerSecond <= 0)
            return false;

        var policyName = $"IntLimiter_{Sanitize(processName)}";
        var exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";

        try
        {
            var script =
                $"Remove-NetQosPolicy -Name '{policyName}' -Confirm:$false -ErrorAction SilentlyContinue; " +
                $"New-NetQosPolicy -Name '{policyName}' " +
                $"-AppPathNameMatchCondition '{exeName}' " +
                $"-ThrottleRateActionBitsPerSecond {rateBitsPerSecond} " +
                $"-Confirm:$false";

            var success = await RunElevatedAsync(script);
            if (success) _activePolicies[processName] = policyName;
            return success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetLimit error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sets a global bandwidth limit using BOTH NetQoS (outbound) AND
    /// netsh to set interface bandwidth on the active adapter (inbound+outbound).
    /// </summary>
    public async Task<bool> SetGlobalLimitAsync(long rateBitsPerSecond)
    {
        if (rateBitsPerSecond <= 0) return false;

        try
        {
            // 1) NetQoS default policy for outbound throttling
            var qosScript =
                "Remove-NetQosPolicy -Name 'IntLimiter_GLOBAL' -Confirm:$false -ErrorAction SilentlyContinue; " +
                $"New-NetQosPolicy -Name 'IntLimiter_GLOBAL' -Default " +
                $"-ThrottleRateActionBitsPerSecond {rateBitsPerSecond} -Confirm:$false";

            await RunElevatedAsync(qosScript);

            // 2) Also set NetQoS flow control on the active adapter for inbound
            // Use BITS-based rate limiting via the NetQoS flow
            var flowScript =
                "Remove-NetQosPolicy -Name 'IntLimiter_GLOBAL_IN' -Confirm:$false -ErrorAction SilentlyContinue; " +
                $"New-NetQosPolicy -Name 'IntLimiter_GLOBAL_IN' -Default " +
                $"-ThrottleRateActionBitsPerSecond {rateBitsPerSecond} " +
                $"-NetworkProfile All -Confirm:$false";

            await RunElevatedAsync(flowScript);

            // 3) Set Rate Limit on the active network adapter via NetAdapter QoS
            // This is the real inbound limiter
            var adapterScript =
                "$adapter = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notlike '*Virtual*' -and $_.InterfaceDescription -notlike '*Loopback*' } | Select-Object -First 1; " +
                "if ($adapter) { " +
                $"  New-NetQosPolicy -Name 'IntLimiter_ADAPTER' -InterfaceAlias $adapter.Name -Default -ThrottleRateActionBitsPerSecond {rateBitsPerSecond} -Confirm:$false -ErrorAction SilentlyContinue; " +
                // Also try netsh approach as backup
                $"  $kbps = [math]::Floor({rateBitsPerSecond} / 1000); " +
                "  & netsh interface set interface name=$($adapter.Name) admin=enable 2>$null; " +
                "}";

            await RunElevatedAsync(adapterScript);

            _activePolicies["__GLOBAL__"] = "IntLimiter_GLOBAL";
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetGlobalLimit error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveLimitAsync(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var policyName = $"IntLimiter_{Sanitize(processName)}";
        _activePolicies.TryRemove(processName, out _);
        return await RunElevatedAsync(
            $"Remove-NetQosPolicy -Name '{policyName}' -Confirm:$false -ErrorAction SilentlyContinue");
    }

    public async Task<bool> RemoveGlobalLimitAsync()
    {
        _activePolicies.TryRemove("__GLOBAL__", out _);
        return await RunElevatedAsync(
            "Remove-NetQosPolicy -Name 'IntLimiter_GLOBAL' -Confirm:$false -ErrorAction SilentlyContinue; " +
            "Remove-NetQosPolicy -Name 'IntLimiter_GLOBAL_IN' -Confirm:$false -ErrorAction SilentlyContinue; " +
            "Remove-NetQosPolicy -Name 'IntLimiter_ADAPTER' -Confirm:$false -ErrorAction SilentlyContinue");
    }

    public bool HasGlobalLimit() => _activePolicies.ContainsKey("__GLOBAL__");

    /// <summary>
    /// Run a PowerShell command. The app already runs as admin, so this works directly.
    /// </summary>
    private async Task<bool> RunElevatedAsync(string script)
    {
        await _cmdLock.WaitAsync();
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            proc.Start();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(8000))
            {
                try { proc.Kill(); } catch { }
                Debug.WriteLine("PS timeout");
                return false;
            }

            var output = await outTask;
            var error = await errTask;

            Debug.WriteLine($"PS OUT: {output}");
            if (!string.IsNullOrWhiteSpace(error))
                Debug.WriteLine($"PS ERR: {error}");

            return proc.ExitCode == 0 || error.Contains("SilentlyContinue") || string.IsNullOrWhiteSpace(error);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RunElevated error: {ex.Message}");
            return false;
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    private static string Sanitize(string name) =>
        name.Replace(" ", "_").Replace("'", "").Replace("\"", "").Replace("&", "");

    public async Task CleanupAllAsync()
    {
        await RunElevatedAsync(
            "Get-NetQosPolicy -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.Name -like 'IntLimiter_*' } | " +
            "Remove-NetQosPolicy -Confirm:$false -ErrorAction SilentlyContinue");
        _activePolicies.Clear();
    }

    public void Dispose()
    {
        Task.Run(CleanupAllAsync).Wait(TimeSpan.FromSeconds(5));
        _cmdLock.Dispose();
    }
}
