namespace IntLimiter.Core.Models;

public enum LimiterMode
{
    Stopped,
    Monitoring,
    WinDivert,
    QosPolicyFallback,
    Error
}

public sealed record LimiterRuntimeStatus
{
    public LimiterMode Mode { get; init; } = LimiterMode.Stopped;
    public bool IsRunning { get; init; }
    public bool IsAdmin { get; init; }
    public bool WinDivertReady { get; init; }
    public string Message { get; init; } = "";
    public string LastError { get; init; } = "";
    public int ActiveRuleCount { get; init; }
    public int QueuedPacketCount { get; init; }
    public long QueuedBytes { get; init; }
    public long CapturedPackets { get; init; }
    public long DelayedPackets { get; init; }
    public long ReinjectedPackets { get; init; }
    public long DroppedPackets { get; init; }
    public long ProcessMappingSuccess { get; init; }
    public long ProcessMappingFailed { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Level { get; init; } = "Information";
    public string Event { get; init; } = "";
    public string Source { get; init; } = "IntLimiter";
    public string Message { get; init; } = "";
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
}

public sealed record ServiceDiagnosticsDto
{
    public LimiterMode RuntimeMode { get; init; } = LimiterMode.Stopped;
    public bool IsRunning { get; init; }
    public bool IsAdmin { get; init; }
    public bool WinDivertLoaded { get; init; }
    public bool QosFallbackActive { get; init; }
    public int ActiveRuleCount { get; init; }
    public int QueueLength { get; init; }
    public long QueuedBytes { get; init; }
    public long CapturedPackets { get; init; }
    public long DelayedPackets { get; init; }
    public long ReinjectedPackets { get; init; }
    public long DroppedPackets { get; init; }
    public long ProcessMappingSuccess { get; init; }
    public long ProcessMappingFailed { get; init; }
    public string LastError { get; init; } = "";
    public string Message { get; init; } = "";
    public TimeSpan ServiceUptime { get; init; }
    public string LogPath { get; init; } = "";
    public string RuleStorePath { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
