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
    public DateTime LastSeenUtc { get; set; }
    public DateTime LastActivityUtc { get; set; }
    public string SelectionKey => !string.IsNullOrWhiteSpace(ExecutablePath)
        ? ExecutablePath
        : $"{ProcessName}|{ProcessId}";
    public string DisplayDownloadRate => FormatRate(DownloadBytesPerSecond);
    public string DisplayUploadRate => FormatRate(UploadBytesPerSecond);
    public string DisplayIdentity => $"PID {ProcessId}";

    private static string FormatRate(long bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{NetworkUnitConverter.ConvertFromBytesPerSecond(bytesPerSecond, NetworkUnit.MegabytesPerSecond):0.00} MB/s";
        }

        if (bytesPerSecond >= 1024)
        {
            return $"{NetworkUnitConverter.ConvertFromBytesPerSecond(bytesPerSecond, NetworkUnit.KilobytesPerSecond):0.0} KB/s";
        }

        return $"{bytesPerSecond} B/s";
    }
}
