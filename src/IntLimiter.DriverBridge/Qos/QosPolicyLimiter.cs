using System.Diagnostics;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Infrastructure;
using IntLimiter.Core.Models;

namespace IntLimiter.DriverBridge.Qos;

public sealed class QosPolicyLimiter : ITrafficLimiter
{
    private const string PolicyPrefix = "IntLimiter_";
    private readonly IAppLog _log;
    private IReadOnlyList<BandwidthRule> _rules = [];
    private LimiterRuntimeStatus _status = new()
    {
        Mode = LimiterMode.Stopped,
        IsAdmin = Privilege.IsAdministrator(),
        Message = "QoS fallback stopped"
    };

    public QosPolicyLimiter(IAppLog log)
    {
        _log = log;
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken)
    {
        await RemoveAllPoliciesAsync(cancellationToken);
        var qosRules = rules.Where(IsSupportedQosRule).ToArray();
        foreach (var rule in qosRules)
        {
            await CreatePolicyAsync(rule, cancellationToken);
        }

        _rules = qosRules;
        _status = new LimiterRuntimeStatus
        {
            Mode = LimiterMode.QosPolicyFallback,
            IsRunning = qosRules.Length > 0,
            IsAdmin = Privilege.IsAdministrator(),
            WinDivertReady = false,
            ActiveRuleCount = qosRules.Length,
            Message = qosRules.Length == 0
                ? "QoS fallback has no supported upload/process rules to apply."
                : $"QoS fallback active with {qosRules.Length} outbound policy/policies.",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _log.Event("Warning", "QosFallbackModeActive", nameof(QosPolicyLimiter), _status.Message,
            new Dictionary<string, object?> { ["ruleCount"] = qosRules.Length });
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        await RemoveAllPoliciesAsync(cancellationToken);
        _rules = [];
        _status = _status with
        {
            Mode = LimiterMode.Stopped,
            IsRunning = false,
            ActiveRuleCount = 0,
            Message = "QoS fallback stopped",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _log.Event("Information", "StopAllLimitsExecuted", nameof(QosPolicyLimiter), "QoS fallback stopped and IntLimiter policies removed.");
    }

    public LimiterRuntimeStatus GetStatus() => _status with
    {
        IsAdmin = Privilege.IsAdministrator(),
        ActiveRuleCount = _rules.Count,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public IReadOnlyList<ProcessIdentity> GetTrafficSnapshots() => [];

    public Task ShutdownAsync(CancellationToken cancellationToken) => StopAllAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None);
    }

    private static bool IsSupportedQosRule(BandwidthRule rule)
    {
        if (!rule.Enabled || !rule.IsValid)
        {
            return false;
        }

        if (rule.Direction == TrafficDirection.Download)
        {
            return false;
        }

        return rule.Scope is RuleScopeKind.ProcessName or RuleScopeKind.ProcessPath or RuleScopeKind.Pid;
    }

    private async Task CreatePolicyAsync(BandwidthRule rule, CancellationToken cancellationToken)
    {
        var app = GetAppPathCondition(rule);
        if (string.IsNullOrWhiteSpace(app))
        {
            _log.Warning(nameof(QosPolicyLimiter), $"Skipped QoS rule {rule.Name}: process executable is unknown.");
            return;
        }

        var policyName = PolicyPrefix + rule.RuleId.ToString("N");
        var bitsPerSecond = checked(rule.LimitBytesPerSecond * 8);
        var script = "New-NetQosPolicy "
            + $"-Name '{Escape(policyName)}' "
            + "-PolicyStore ActiveStore "
            + "-NetworkProfile All "
            + $"-AppPathNameMatchCondition '{Escape(app)}' "
            + "-IPProtocolMatchCondition Both "
            + $"-ThrottleRateActionBitsPerSecond {bitsPerSecond} | Out-Null";

        await RunPowerShellAsync(script, cancellationToken);
        _log.Event("Information", "RuleApplied", nameof(QosPolicyLimiter), $"Created QoS policy {policyName} for {app} at {rule.LimitBytesPerSecond} B/s.",
            new Dictionary<string, object?>
            {
                ["policyName"] = policyName,
                ["app"] = app,
                ["limitBytesPerSecond"] = rule.LimitBytesPerSecond
            });
    }

    private static string? GetAppPathCondition(BandwidthRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.ProcessPath))
        {
            return rule.ProcessPath;
        }

        if (!string.IsNullOrWhiteSpace(rule.ProcessName))
        {
            return rule.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? rule.ProcessName
                : rule.ProcessName + ".exe";
        }

        return null;
    }

    private static async Task RemoveAllPoliciesAsync(CancellationToken cancellationToken)
    {
        var script = $"Get-NetQosPolicy -PolicyStore ActiveStore | Where-Object {{ $_.Name -like '{PolicyPrefix}*' }} | ForEach-Object {{ Remove-NetQosPolicy -Name $_.Name -PolicyStore ActiveStore -Confirm:$false }}";
        await RunPowerShellAsync(script, cancellationToken);
    }

    private static async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteArgument(script),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start powershell.exe.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell QoS command failed ({process.ExitCode}). {error} {output}".Trim());
        }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
