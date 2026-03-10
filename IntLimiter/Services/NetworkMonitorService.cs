using IntLimiter.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IntLimiter.Services;

public class NetworkMonitorService : IDisposable
{
    private TraceEventSession? _session;
    private ETWTraceEventSource? _source;
    private Thread? _processingThread;
    private readonly ConcurrentDictionary<int, ProcessNetworkInfo> _processMap = new();
    private readonly DispatcherTimer _updateTimer;
    private readonly Dispatcher _dispatcher;
    private volatile bool _isRunning;

    // Smoothing: keep track of previous speeds to avoid flickering
    private readonly ConcurrentDictionary<int, (long lastDown, long lastUp, int zeroCount)> _smoothing = new();

    public event Action? ProcessListUpdated;
    public IReadOnlyDictionary<int, ProcessNetworkInfo> ProcessMap => _processMap;
    public long TotalDownloadSpeed { get; private set; }
    public long TotalUploadSpeed { get; private set; }

    public NetworkMonitorService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        try
        {
            var sessionName = "IntLimiterETWSession";
            try
            {
                var existing = TraceEventSession.GetActiveSession(sessionName);
                existing?.Stop(true);
                existing?.Dispose();
            }
            catch { }

            _session = new TraceEventSession(sessionName) { StopOnDispose = true };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            _source = _session.Source;

            _source.Kernel.TcpIpSend += d => SafeRecord(d.ProcessID, 0, d.size);
            _source.Kernel.TcpIpRecv += d => SafeRecord(d.ProcessID, d.size, 0);
            _source.Kernel.TcpIpSendIPV6 += d => SafeRecord(d.ProcessID, 0, d.size);
            _source.Kernel.TcpIpRecvIPV6 += d => SafeRecord(d.ProcessID, d.size, 0);
            _source.Kernel.UdpIpSend += d => SafeRecord(d.ProcessID, 0, d.size);
            _source.Kernel.UdpIpRecv += d => SafeRecord(d.ProcessID, d.size, 0);
            _source.Kernel.UdpIpSendIPV6 += d => SafeRecord(d.ProcessID, 0, d.size);
            _source.Kernel.UdpIpRecvIPV6 += d => SafeRecord(d.ProcessID, d.size, 0);

            _processingThread = new Thread(() =>
            {
                try { _source?.Process(); }
                catch { }
            })
            { IsBackground = true, Name = "ETW Network Monitor" };
            _processingThread.Start();
            _updateTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ETW start failed: {ex.Message}");
            _isRunning = false;
        }
    }

    private void SafeRecord(int pid, int downloadBytes, int uploadBytes)
    {
        try
        {
            if (pid <= 0 || !_isRunning) return;

            var info = _processMap.GetOrAdd(pid, id =>
            {
                var pi = new ProcessNetworkInfo { ProcessId = id };
                try
                {
                    _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        try
                        {
                            using var proc = Process.GetProcessById(id);
                            pi.ProcessName = proc.ProcessName;
                            try
                            {
                                pi.FilePath = proc.MainModule?.FileName ?? "";
                                if (!string.IsNullOrEmpty(pi.FilePath))
                                    pi.Icon = GetProcessIcon(pi.FilePath);
                            }
                            catch { }
                        }
                        catch { pi.ProcessName = $"PID {id}"; }
                    });
                }
                catch { pi.ProcessName = $"PID {id}"; }
                return pi;
            });

            Interlocked.Add(ref info._pendingDown, downloadBytes);
            Interlocked.Add(ref info._pendingUp, uploadBytes);
        }
        catch { }
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            long totalDown = 0, totalUp = 0;
            var toRemove = new List<int>();

            foreach (var kvp in _processMap)
            {
                var info = kvp.Value;
                var pid = kvp.Key;

                var rawDown = (long)Interlocked.Exchange(ref info._pendingDown, 0) * 2;
                var rawUp = (long)Interlocked.Exchange(ref info._pendingUp, 0) * 2;

                // Smoothing logic: don't immediately drop to 0
                // If current reading is 0 but we had traffic before, show decayed value
                var prev = _smoothing.GetOrAdd(pid, _ => (0L, 0L, 0));

                long smoothDown, smoothUp;

                if (rawDown > 0)
                {
                    smoothDown = rawDown;
                    _smoothing[pid] = (rawDown, prev.lastUp, 0);
                }
                else if (prev.lastDown > 0 && prev.zeroCount < 3)
                {
                    // Decay: show half of previous value for up to 3 ticks
                    smoothDown = prev.lastDown / 2;
                    if (smoothDown < 100) smoothDown = 0; // below threshold, show 0
                    _smoothing[pid] = (smoothDown, prev.lastUp, prev.zeroCount + 1);
                }
                else
                {
                    smoothDown = 0;
                    _smoothing[pid] = (0, prev.lastUp, 0);
                }

                prev = _smoothing[pid]; // re-read after down update

                if (rawUp > 0)
                {
                    smoothUp = rawUp;
                    _smoothing[pid] = (prev.lastDown, rawUp, prev.zeroCount);
                }
                else if (prev.lastUp > 0 && prev.zeroCount < 3)
                {
                    smoothUp = prev.lastUp / 2;
                    if (smoothUp < 100) smoothUp = 0;
                    _smoothing[pid] = (prev.lastDown, smoothUp, prev.zeroCount + 1);
                }
                else
                {
                    smoothUp = 0;
                    _smoothing[pid] = (prev.lastDown, 0, prev.zeroCount);
                }

                info.DownloadSpeed = smoothDown;
                info.UploadSpeed = smoothUp;
                info.TotalDownloaded += rawDown / 2; // actual bytes (undo the x2)
                info.TotalUploaded += rawUp / 2;

                totalDown += smoothDown;
                totalUp += smoothUp;

                // Cleanup dead processes
                if (smoothDown == 0 && smoothUp == 0 && info.TotalDownloaded == 0 && info.TotalUploaded == 0)
                {
                    try { Process.GetProcessById(pid); }
                    catch
                    {
                        toRemove.Add(pid);
                        _smoothing.TryRemove(pid, out _);
                    }
                }
            }

            foreach (var pid in toRemove)
                _processMap.TryRemove(pid, out _);

            TotalDownloadSpeed = totalDown;
            TotalUploadSpeed = totalUp;
            ProcessListUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update error: {ex.Message}");
        }
    }

    private static ImageSource? GetProcessIcon(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;
            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public void Stop()
    {
        _isRunning = false;
        _updateTimer.Stop();
        try { _session?.Stop(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        _source = null;
    }

    public void Dispose() => Stop();
}
