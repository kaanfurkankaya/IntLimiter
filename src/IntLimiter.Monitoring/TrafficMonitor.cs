using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntLimiter.Monitoring;

public class TrafficMonitor : ITrafficMonitor
{
    private const int SamplingIntervalMilliseconds = 1000;
    private const int RateWindowSamples = 4;
    private static readonly TimeSpan ProcessRetention = TimeSpan.FromSeconds(8);

    private TraceEventSession? _session;
    private readonly ConcurrentDictionary<int, ProcessNetworkUsage> _processes = new();
    private readonly Dictionary<int, SlidingAverageWindow> _rateWindows = new();
    private readonly object _snapshotLock = new();
    private readonly SlidingAverageWindow _globalRateWindow = new(RateWindowSamples);
    private Task? _etwTask;
    private Task? _updateTask;
    private CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private string _sessionName = $"IntLimiter_TrafficMonitor_{Environment.ProcessId}";

    public bool IsRunning { get; private set; }
    public bool RequiresAdministrator { get; private set; }
    public string StatusMessage { get; private set; } = "Monitoring has not started.";

    public long GlobalDownloadBytesPerSecond { get; private set; }
    public long GlobalUploadBytesPerSecond { get; private set; }

    public event EventHandler? TrafficUpdated;

    public void Start()
    {
        lock (_stateLock)
        {
            if (IsRunning || _etwTask is { IsCompleted: false })
            {
                return;
            }
        }

        _processes.Clear();
        lock (_snapshotLock)
        {
            _rateWindows.Clear();
            _globalRateWindow.Reset();
        }
        _bytesDownloadedThisSecond.Clear();
        _bytesUploadedThisSecond.Clear();
        GlobalDownloadBytesPerSecond = 0;
        GlobalUploadBytesPerSecond = 0;
        _cts = new CancellationTokenSource();
        RefreshProcessSnapshotOnly(DateTime.UtcNow);

        if (TraceEventSession.IsElevated() != true)
        {
            IsRunning = false;
            RequiresAdministrator = true;
            StatusMessage = $"Live per-process traffic is blocked without Administrator rights. Showing running processes only. Rates use a {RateWindowSamples}-second moving average when ETW is active.";
            _updateTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(SamplingIntervalMilliseconds, _cts.Token);
                    RefreshProcessSnapshotOnly(DateTime.UtcNow);
                    RaiseTrafficUpdated();
                }
            }, _cts.Token);
            RaiseTrafficUpdated();
            return;
        }

        RequiresAdministrator = false;
        IsRunning = true;
        StatusMessage = "Monitoring active. Waiting for network traffic...";

        _etwTask = Task.Run(() => StartEtwSession(_cts.Token), _cts.Token);

        // Fire TrafficUpdated every second and publish a fresh snapshot.
        _updateTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(SamplingIntervalMilliseconds, _cts.Token);
                CalculateRates();
                RaiseTrafficUpdated();
            }
        }, _cts.Token);

        RaiseTrafficUpdated();
    }

    private void StartEtwSession(CancellationToken cancellationToken)
    {
        try
        {
            using (_session = new TraceEventSession(_sessionName))
            {
                _session.StopOnDispose = true;
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpRecv += data => UpdateUsage(data.ProcessID, data.size, true);
                _session.Source.Kernel.TcpIpSend += data => UpdateUsage(data.ProcessID, data.size, false);
                _session.Source.Kernel.UdpIpRecv += data => UpdateUsage(data.ProcessID, data.size, true);
                _session.Source.Kernel.UdpIpSend += data => UpdateUsage(data.ProcessID, data.size, false);

                _session.Source.Process();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            IsRunning = false;
            StatusMessage = $"Monitoring failed: {ex.Message}";
            RaiseTrafficUpdated();
        }
    }

    private readonly ConcurrentDictionary<int, long> _bytesDownloadedThisSecond = new();
    private readonly ConcurrentDictionary<int, long> _bytesUploadedThisSecond = new();

    private void UpdateUsage(int processId, int bytes, bool isDownload)
    {
        if (processId <= 0) return;
        
        if (isDownload)
            _bytesDownloadedThisSecond.AddOrUpdate(processId, bytes, (_, old) => old + bytes);
        else
            _bytesUploadedThisSecond.AddOrUpdate(processId, bytes, (_, old) => old + bytes);
    }

    private void CalculateRates()
    {
        var now = DateTime.UtcNow;
        var runningProcesses = EnumerateProcesses(now);
        var rawDownloads = DrainCounterSnapshot(_bytesDownloadedThisSecond);
        var rawUploads = DrainCounterSnapshot(_bytesUploadedThisSecond);

        long rawTotalDown = 0;
        long rawTotalUp = 0;

        lock (_snapshotLock)
        {
            var processIds = _processes.Keys
                .Union(runningProcesses.Keys)
                .Union(rawDownloads.Keys)
                .Union(rawUploads.Keys)
                .ToArray();

            foreach (var processId in processIds)
            {
                rawDownloads.TryGetValue(processId, out var rawDown);
                rawUploads.TryGetValue(processId, out var rawUp);

                rawTotalDown += rawDown;
                rawTotalUp += rawUp;

                var usage = _processes.GetOrAdd(processId, id => CreateProcessUsageSnapshot(id, now));
                if (runningProcesses.TryGetValue(processId, out var snapshot))
                {
                    usage.ProcessName = snapshot.ProcessName;
                    usage.ExecutablePath = snapshot.ExecutablePath;
                    usage.LastSeenUtc = snapshot.LastSeenUtc;
                }

                if (!_rateWindows.TryGetValue(processId, out var window))
                {
                    window = new SlidingAverageWindow(RateWindowSamples);
                    _rateWindows[processId] = window;
                }

                window.AddSample(rawDown, rawUp);
                usage.DownloadBytesPerSecond = window.AverageDownloadBytesPerSecond;
                usage.UploadBytesPerSecond = window.AverageUploadBytesPerSecond;
                usage.TotalDownloadBytes += rawDown;
                usage.TotalUploadBytes += rawUp;

                if (rawDown > 0 || rawUp > 0)
                {
                    usage.LastActivityUtc = now;
                }
            }

            _globalRateWindow.AddSample(rawTotalDown, rawTotalUp);
            GlobalDownloadBytesPerSecond = _globalRateWindow.AverageDownloadBytesPerSecond;
            GlobalUploadBytesPerSecond = _globalRateWindow.AverageUploadBytesPerSecond;

            PruneInactiveProcesses(now, runningProcesses.Keys);
        }

        if (IsRunning)
        {
            StatusMessage = GlobalDownloadBytesPerSecond > 0 || GlobalUploadBytesPerSecond > 0
                ? $"Monitoring active. Rates use a {RateWindowSamples}-second moving average."
                : $"Monitoring active. Waiting for network traffic. Rates use a {RateWindowSamples}-second moving average.";
        }
    }

    private void RefreshProcessSnapshotOnly(DateTime snapshotTimeUtc)
    {
        var runningProcesses = EnumerateProcesses(snapshotTimeUtc);

        lock (_snapshotLock)
        {
            foreach (var snapshot in runningProcesses.Values)
            {
                var usage = _processes.GetOrAdd(snapshot.ProcessId, _ => new ProcessNetworkUsage
                {
                    ProcessId = snapshot.ProcessId,
                    ProcessName = snapshot.ProcessName,
                    ExecutablePath = snapshot.ExecutablePath,
                    LastSeenUtc = snapshot.LastSeenUtc,
                    LastActivityUtc = DateTime.MinValue
                });

                usage.ProcessName = snapshot.ProcessName;
                usage.ExecutablePath = snapshot.ExecutablePath;
                usage.LastSeenUtc = snapshot.LastSeenUtc;
            }

            foreach (var existing in _processes.ToArray())
            {
                if (!runningProcesses.ContainsKey(existing.Key) && existing.Value.DownloadBytesPerSecond == 0 && existing.Value.UploadBytesPerSecond == 0)
                {
                    _processes.TryRemove(existing.Key, out _);
                    _rateWindows.Remove(existing.Key);
                }
            }
        }
    }

    private static Dictionary<int, long> DrainCounterSnapshot(ConcurrentDictionary<int, long> source)
    {
        var snapshot = new Dictionary<int, long>();
        foreach (var key in source.Keys)
        {
            if (source.TryRemove(key, out var value))
            {
                snapshot[key] = value;
            }
        }

        return snapshot;
    }

    private Dictionary<int, ProcessNetworkUsage> EnumerateProcesses(DateTime snapshotTimeUtc)
    {
        var snapshots = new Dictionary<int, ProcessNetworkUsage>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                snapshots[process.Id] = new ProcessNetworkUsage
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    ExecutablePath = TryGetProcessPath(process),
                    LastSeenUtc = snapshotTimeUtc,
                    LastActivityUtc = DateTime.MinValue
                };
            }
            catch
            {
                // Ignore processes that disappear during enumeration.
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshots;
    }

    private void PruneInactiveProcesses(DateTime now, IEnumerable<int> runningProcessIds)
    {
        var running = runningProcessIds.ToHashSet();
        foreach (var entry in _processes.ToArray())
        {
            var processId = entry.Key;
            var process = entry.Value;
            var stillRunning = running.Contains(processId);
            var stale = now - process.LastActivityUtc > ProcessRetention;

            if (!stillRunning && stale)
            {
                _processes.TryRemove(processId, out _);
                _rateWindows.Remove(processId);
            }
        }
    }

    private ProcessNetworkUsage CreateProcessUsageSnapshot(int processId, DateTime snapshotTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessNetworkUsage
            {
                ProcessId = processId,
                ProcessName = process.ProcessName,
                ExecutablePath = TryGetProcessPath(process),
                LastSeenUtc = snapshotTimeUtc,
                LastActivityUtc = snapshotTimeUtc
            };
        }
        catch
        {
            return new ProcessNetworkUsage
            {
                ProcessId = processId,
                ProcessName = "Unknown",
                ExecutablePath = string.Empty,
                LastSeenUtc = snapshotTimeUtc,
                LastActivityUtc = DateTime.MinValue
            };
        }
    }

    public IEnumerable<ProcessNetworkUsage> GetActiveProcesses()
    {
        lock (_snapshotLock)
        {
            return _processes.Values
                .Select(process => new ProcessNetworkUsage
                {
                    ProcessId = process.ProcessId,
                    ProcessName = process.ProcessName,
                    ExecutablePath = process.ExecutablePath,
                    DownloadBytesPerSecond = process.DownloadBytesPerSecond,
                    UploadBytesPerSecond = process.UploadBytesPerSecond,
                    TotalDownloadBytes = process.TotalDownloadBytes,
                    TotalUploadBytes = process.TotalUploadBytes,
                    LastSeenUtc = process.LastSeenUtc,
                    LastActivityUtc = process.LastActivityUtc
                })
                .ToList();
        }
    }

    public void Stop()
    {
        IsRunning = false;
        _cts.Cancel();
        _session?.Dispose();
        StatusMessage = RequiresAdministrator
            ? "Live monitoring is blocked. Restart IntLimiter as Administrator to start the ETW network session."
            : "Monitoring stopped.";
        RaiseTrafficUpdated();
    }

    public void Dispose()
    {
        Stop();
    }

    private void RaiseTrafficUpdated()
    {
        TrafficUpdated?.Invoke(this, EventArgs.Empty);
    }

    private static string TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class SlidingAverageWindow
    {
        private readonly int _capacity;
        private readonly Queue<long> _downloads = new();
        private readonly Queue<long> _uploads = new();
        private long _downloadSum;
        private long _uploadSum;

        public SlidingAverageWindow(int capacity)
        {
            _capacity = capacity;
        }

        public long AverageDownloadBytesPerSecond => _downloads.Count == 0 ? 0 : _downloadSum / _downloads.Count;
        public long AverageUploadBytesPerSecond => _uploads.Count == 0 ? 0 : _uploadSum / _uploads.Count;

        public void AddSample(long downloadBytes, long uploadBytes)
        {
            _downloads.Enqueue(downloadBytes);
            _uploads.Enqueue(uploadBytes);
            _downloadSum += downloadBytes;
            _uploadSum += uploadBytes;

            while (_downloads.Count > _capacity)
            {
                _downloadSum -= _downloads.Dequeue();
            }

            while (_uploads.Count > _capacity)
            {
                _uploadSum -= _uploads.Dequeue();
            }
        }

        public void Reset()
        {
            _downloads.Clear();
            _uploads.Clear();
            _downloadSum = 0;
            _uploadSum = 0;
        }
    }
}
