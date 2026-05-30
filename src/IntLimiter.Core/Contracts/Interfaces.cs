using IntLimiter.Core.Models;

namespace IntLimiter.Core.Contracts;

public interface IRuleStore
{
    Task<IReadOnlyList<BandwidthRule>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken);
}

public interface IProcessNetworkMonitor
{
    Task<IReadOnlyList<ProcessIdentity>> GetProcessesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NetworkFlow>> GetTcpFlowsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NetworkFlow>> GetUdpFlowsAsync(CancellationToken cancellationToken);
    Task<NetworkFlow?> FindTcpFlowAsync(PacketFlowKey key, CancellationToken cancellationToken);
    Task<NetworkFlow?> FindUdpFlowAsync(PacketFlowKey key, CancellationToken cancellationToken);
}

public interface ITrafficLimiter : IAsyncDisposable
{
    Task StartMonitoringAsync(CancellationToken cancellationToken);
    Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken);
    Task StopAllAsync(CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
    LimiterRuntimeStatus GetStatus();
    IReadOnlyList<ProcessIdentity> GetTrafficSnapshots();
}

public interface IServiceControlClient
{
    Task<ServiceStateDto> GetStateAsync(CancellationToken cancellationToken);
    Task<ServiceDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProcessIdentity>> GetProcessesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BandwidthRule>> GetRulesAsync(CancellationToken cancellationToken);
    Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken);
    Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken);
    Task StopAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LogEntry>> GetLogsAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<LogEntry>> GetRecentLogsAsync(int take, CancellationToken cancellationToken);
}

public interface IAppLog
{
    void Information(string source, string message);
    void Warning(string source, string message);
    void Error(string source, string message);
    void Event(string level, string eventName, string source, string message, IReadOnlyDictionary<string, object?>? data = null);
    IReadOnlyList<LogEntry> ReadRecent(int take);
}

public sealed record ServiceStateDto
{
    public LimiterRuntimeStatus Runtime { get; init; } = new();
    public IReadOnlyList<BandwidthRule> Rules { get; init; } = [];
    public IReadOnlyList<LogEntry> Logs { get; init; } = [];
}
