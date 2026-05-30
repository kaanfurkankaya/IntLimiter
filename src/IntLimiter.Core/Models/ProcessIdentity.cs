namespace IntLimiter.Core.Models;

public sealed record ProcessIdentity
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string? ExecutablePath { get; init; }
    public long UploadBytesPerSecond { get; init; }
    public long DownloadBytesPerSecond { get; init; }
}
