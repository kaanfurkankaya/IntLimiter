using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using IntLimiter.DriverBridge.Qos;
using IntLimiter.DriverBridge.WinDivert;

namespace IntLimiter.DriverBridge;

public sealed class HybridTrafficLimiter : ITrafficLimiter
{
    private readonly WinDivertTrafficLimiter _winDivert;
    private readonly QosPolicyLimiter _qosPolicy;
    private readonly IAppLog _log;
    private ITrafficLimiter? _activeLimiter;

    public HybridTrafficLimiter(WinDivertTrafficLimiter winDivert, QosPolicyLimiter qosPolicy, IAppLog log)
    {
        _winDivert = winDivert;
        _qosPolicy = qosPolicy;
        _log = log;
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _winDivert.StartMonitoringAsync(cancellationToken);
            _activeLimiter = null;
        }
        catch (Exception ex)
        {
            _log.Event("Warning", "WinDivertMonitoringFailed", nameof(HybridTrafficLimiter), $"WinDivert passive monitoring could not be started: {ex.Message}",
                new Dictionary<string, object?> { ["reason"] = ex.Message });
        }
    }

    public async Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken)
    {
        try
        {
            await _qosPolicy.StopAllAsync(cancellationToken);
            await _winDivert.ApplyRulesAsync(rules, cancellationToken);
            _activeLimiter = _winDivert;
        }
        catch (Exception ex)
        {
            _log.Event("Warning", "QosFallbackModeActive", nameof(HybridTrafficLimiter), $"WinDivert unavailable; falling back to Windows QoS outbound policies. Reason: {ex.Message}",
                new Dictionary<string, object?> { ["reason"] = ex.Message });
            await _winDivert.StopAllAsync(CancellationToken.None);
            await _qosPolicy.ApplyRulesAsync(rules, cancellationToken);
            _activeLimiter = _qosPolicy;
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        await _qosPolicy.StopAllAsync(cancellationToken);
        await _winDivert.StopAllAsync(cancellationToken);
        _activeLimiter = null;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _winDivert.ShutdownAsync(cancellationToken);
        await _qosPolicy.ShutdownAsync(cancellationToken);
        _activeLimiter = null;
    }

    public LimiterRuntimeStatus GetStatus()
    {
        if (_activeLimiter is not null)
        {
            return _activeLimiter.GetStatus();
        }

        var winDivertStatus = _winDivert.GetStatus();
        if (winDivertStatus.IsRunning || winDivertStatus.Mode == LimiterMode.Error)
        {
            return winDivertStatus;
        }

        var qosStatus = _qosPolicy.GetStatus();
        return qosStatus.IsRunning ? qosStatus : winDivertStatus;
    }

    public IReadOnlyList<ProcessIdentity> GetTrafficSnapshots()
    {
        var snapshots = _winDivert.GetTrafficSnapshots();
        return snapshots.Count > 0 ? snapshots : _qosPolicy.GetTrafficSnapshots();
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None);
    }
}
