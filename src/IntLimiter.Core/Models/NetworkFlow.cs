namespace IntLimiter.Core.Models;

public enum IpProtocol
{
    Tcp = 6,
    Udp = 17
}

public sealed record NetworkFlow
{
    public IpProtocol Protocol { get; init; }
    public string LocalAddress { get; init; } = "";
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = "";
    public int RemotePort { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string? ProcessPath { get; init; }
}

public readonly record struct PacketFlowKey(
    IpProtocol Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort);
