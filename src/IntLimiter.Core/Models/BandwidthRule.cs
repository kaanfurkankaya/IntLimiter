namespace IntLimiter.Core.Models;

public enum RuleScopeKind
{
    Global,
    ProcessName,
    ProcessPath,
    Pid
}

public enum TrafficDirection
{
    Download,
    Upload,
    Both
}

public sealed record BandwidthRule
{
    public Guid RuleId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public RuleScopeKind Scope { get; set; }
    public TrafficDirection Direction { get; set; } = TrafficDirection.Both;
    public long LimitBytesPerSecond { get; set; }
    public bool Enabled { get; set; } = true;
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public string? ProcessPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsValid => LimitBytesPerSecond > 0 && Scope switch
    {
        RuleScopeKind.Global => true,
        RuleScopeKind.Pid => ProcessId is > 0,
        RuleScopeKind.ProcessName => !string.IsNullOrWhiteSpace(ProcessName),
        RuleScopeKind.ProcessPath => !string.IsNullOrWhiteSpace(ProcessPath),
        _ => false
    };

    public bool Matches(ProcessIdentity process, TrafficDirection direction)
    {
        if (!Enabled || !IsValid || !MatchesDirection(direction))
        {
            return false;
        }

        return Scope switch
        {
            RuleScopeKind.Global => true,
            RuleScopeKind.Pid => ProcessId == process.ProcessId,
            RuleScopeKind.ProcessName => string.Equals(ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase),
            RuleScopeKind.ProcessPath => !string.IsNullOrWhiteSpace(ProcessPath)
                && string.Equals(ProcessPath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool MatchesDirection(TrafficDirection direction) =>
        Direction == TrafficDirection.Both || Direction == direction;
}
