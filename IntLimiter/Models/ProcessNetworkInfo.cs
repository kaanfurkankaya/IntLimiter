using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace IntLimiter.Models;

public partial class ProcessNetworkInfo : ObservableObject
{
    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private int _processId;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private long _downloadSpeed;

    [ObservableProperty]
    private long _uploadSpeed;

    [ObservableProperty]
    private long _totalDownloaded;

    [ObservableProperty]
    private long _totalUploaded;

    // Limits in bits per second (0 = unlimited)
    [ObservableProperty]
    private long _downloadLimit;

    [ObservableProperty]
    private long _uploadLimit;

    [ObservableProperty]
    private bool _isLimited;

    // Thread-safe accumulators for ETW callbacks (used with Interlocked)
    public int _pendingDown;
    public int _pendingUp;

    public string DisplayName => string.IsNullOrEmpty(ProcessName) ? $"PID {ProcessId}" : ProcessName;
}
