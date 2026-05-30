using IntLimiter.Core.Contracts;
using IntLimiter.Core.Infrastructure;
using IntLimiter.Core.Models;

namespace IntLimiter.Service;

public sealed class LimiterCoordinator
{
    private readonly IRuleStore _ruleStore;
    private readonly ITrafficLimiter _trafficLimiter;
    private readonly IProcessNetworkMonitor _processNetworkMonitor;
    private readonly IAppLog _appLog;
    private readonly ILogger<LimiterCoordinator> _logger;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private IReadOnlyList<BandwidthRule> _rules = [];

    public LimiterCoordinator(
        IRuleStore ruleStore,
        ITrafficLimiter trafficLimiter,
        IProcessNetworkMonitor processNetworkMonitor,
        IAppLog appLog,
        ILogger<LimiterCoordinator> logger)
    {
        _ruleStore = ruleStore;
        _trafficLimiter = trafficLimiter;
        _processNetworkMonitor = processNetworkMonitor;
        _appLog = appLog;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _rules = await _ruleStore.LoadAsync(cancellationToken);
        if (_rules.Any(rule => rule.Enabled))
        {
            await _trafficLimiter.ApplyRulesAsync(_rules, cancellationToken);
        }
        else
        {
            await _trafficLimiter.StartMonitoringAsync(cancellationToken);
        }

        _logger.LogInformation("IntLimiter initialized with {RuleCount} rule(s).", _rules.Count);
        _appLog.Event("Information", "ServiceStarted", nameof(LimiterCoordinator), $"Service started with {_rules.Count} rule(s).",
            new Dictionary<string, object?> { ["ruleCount"] = _rules.Count });
    }

    public ServiceStateDto GetState() => new()
    {
        Runtime = _trafficLimiter.GetStatus(),
        Rules = _rules,
        Logs = _appLog.ReadRecent(100)
    };

    public ServiceDiagnosticsDto GetDiagnostics()
    {
        var runtime = _trafficLimiter.GetStatus();
        return new ServiceDiagnosticsDto
        {
            RuntimeMode = runtime.Mode,
            IsRunning = runtime.IsRunning,
            IsAdmin = runtime.IsAdmin,
            WinDivertLoaded = runtime.WinDivertReady,
            QosFallbackActive = runtime.Mode == LimiterMode.QosPolicyFallback && runtime.IsRunning,
            ActiveRuleCount = runtime.ActiveRuleCount,
            QueueLength = runtime.QueuedPacketCount,
            QueuedBytes = runtime.QueuedBytes,
            CapturedPackets = runtime.CapturedPackets,
            DelayedPackets = runtime.DelayedPackets,
            ReinjectedPackets = runtime.ReinjectedPackets,
            DroppedPackets = runtime.DroppedPackets,
            ProcessMappingSuccess = runtime.ProcessMappingSuccess,
            ProcessMappingFailed = runtime.ProcessMappingFailed,
            LastError = runtime.LastError,
            Message = runtime.Message,
            ServiceUptime = DateTimeOffset.UtcNow - _startedAt,
            LogPath = ApplicationPaths.LogPath,
            RuleStorePath = ApplicationPaths.RuleStorePath,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<ProcessIdentity>> GetProcessesAsync(CancellationToken cancellationToken)
    {
        var processes = await _processNetworkMonitor.GetProcessesAsync(cancellationToken);
        var trafficByPid = _trafficLimiter.GetTrafficSnapshots().ToDictionary(process => process.ProcessId);

        return processes.Select(process =>
            trafficByPid.TryGetValue(process.ProcessId, out var traffic)
                ? process with
                {
                    UploadBytesPerSecond = traffic.UploadBytesPerSecond,
                    DownloadBytesPerSecond = traffic.DownloadBytesPerSecond
                }
                : process).ToArray();
    }

    public Task<IReadOnlyList<BandwidthRule>> GetRulesAsync() =>
        Task.FromResult(_rules);

    public async Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            _rules = rules
                .Where(rule => rule.IsValid)
                .Select(rule => rule with
                {
                    Name = string.IsNullOrWhiteSpace(rule.Name) ? BuildRuleName(rule) : rule.Name.Trim(),
                    CreatedAt = rule.CreatedAt == default ? now : rule.CreatedAt,
                    UpdatedAt = now
                })
                .ToArray();

            await _ruleStore.SaveAsync(_rules, cancellationToken);
            await _trafficLimiter.ApplyRulesAsync(_rules, cancellationToken);
            _logger.LogInformation("Applied {RuleCount} IntLimiter rule(s).", _rules.Count);
            _appLog.Event("Information", "RuleApplied", nameof(LimiterCoordinator), $"Applied {_rules.Count} rule(s).",
                new Dictionary<string, object?> { ["ruleCount"] = _rules.Count });
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            _rules = _rules.Where(rule => rule.RuleId != ruleId).ToArray();
            await _ruleStore.SaveAsync(_rules, cancellationToken);
            await _trafficLimiter.ApplyRulesAsync(_rules, cancellationToken);
            _appLog.Event("Information", "RuleRemoved", nameof(LimiterCoordinator), $"Removed rule {ruleId}.",
                new Dictionary<string, object?> { ["ruleId"] = ruleId });
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            _rules = [];
            await _ruleStore.SaveAsync(_rules, cancellationToken);
            await _trafficLimiter.StopAllAsync(cancellationToken);
            _appLog.Event("Warning", "StopAllLimitsExecuted", nameof(LimiterCoordinator), "Stop all limits executed and persisted rules cleared.");
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _trafficLimiter.ShutdownAsync(cancellationToken);
        _appLog.Event("Information", "ServiceStopped", nameof(LimiterCoordinator), "Service stopped.");
    }

    private static string BuildRuleName(BandwidthRule rule)
    {
        var scope = rule.Scope switch
        {
            RuleScopeKind.Global => "Global",
            RuleScopeKind.Pid => $"PID {rule.ProcessId}",
            RuleScopeKind.ProcessName => rule.ProcessName,
            RuleScopeKind.ProcessPath => Path.GetFileName(rule.ProcessPath),
            _ => "Rule"
        };
        return $"{scope} {rule.Direction} {rule.LimitBytesPerSecond / 1024.0:0.#} KB/s";
    }
}
