namespace IntLimiter.Core.Models;

public class ProcessNetworkUsage
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public long DownloadBytesPerSecond { get; set; }
    public long UploadBytesPerSecond { get; set; }
    public long TotalDownloadBytes { get; set; }
    public long TotalUploadBytes { get; set; }
}
