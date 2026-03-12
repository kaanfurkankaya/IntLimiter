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
    private TraceEventSession? _session;
    private readonly ConcurrentDictionary<int, ProcessNetworkUsage> _processes = new();
    private Task? _etwTask;
    private CancellationTokenSource _cts = new();
    
    public long GlobalDownloadBytesPerSecond { get; private set; }
    public long GlobalUploadBytesPerSecond { get; private set; }

    public event EventHandler? TrafficUpdated;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _etwTask = Task.Run(() => StartEtwSession(), _cts.Token);
        
        // Setup a timer to fire TrafficUpdated every second and reset rates
        Task.Run(async () => {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token);
                CalculateRates();
                TrafficUpdated?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void StartEtwSession()
    {
        if (TraceEventSession.IsElevated() != true)
        {
            throw new UnauthorizedAccessException("Traffic monitoring requires Administrator privileges.");
        }

        using (_session = new TraceEventSession("IntLimiter_TrafficMonitor"))
        {
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            _session.Source.Kernel.TcpIpRecv += (data) =>
            {
                UpdateUsage(data.ProcessID, data.size, true);
            };

            _session.Source.Kernel.TcpIpSend += (data) =>
            {
                UpdateUsage(data.ProcessID, data.size, false);
            };
            
            _session.Source.Kernel.UdpIpRecv += (data) =>
            {
                UpdateUsage(data.ProcessID, data.size, true);
            };

            _session.Source.Kernel.UdpIpSend += (data) =>
            {
                UpdateUsage(data.ProcessID, data.size, false);
            };

            _session.Source.Process();
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
        long totalDown = 0;
        long totalUp = 0;

        foreach (var p in _bytesDownloadedThisSecond.Keys.Union(_bytesUploadedThisSecond.Keys))
        {
            _bytesDownloadedThisSecond.TryGetValue(p, out long down);
            _bytesUploadedThisSecond.TryGetValue(p, out long up);
            
            totalDown += down;
            totalUp += up;

            var usage = _processes.GetOrAdd(p, id => new ProcessNetworkUsage { ProcessId = id, ProcessName = GetProcessName(id) });
            usage.DownloadBytesPerSecond = down;
            usage.UploadBytesPerSecond = up;
            usage.TotalDownloadBytes += down;
            usage.TotalUploadBytes += up;
            
            // Reset for next second
            _bytesDownloadedThisSecond[p] = 0;
            _bytesUploadedThisSecond[p] = 0;
        }

        GlobalDownloadBytesPerSecond = totalDown;
        GlobalUploadBytesPerSecond = totalUp;
    }

    private string GetProcessName(int processId)
    {
        try
        {
            var p = Process.GetProcessById(processId);
            return p.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    public IEnumerable<ProcessNetworkUsage> GetActiveProcesses()
    {
        return _processes.Values.ToList();
    }

    public void Stop()
    {
        _cts.Cancel();
        _session?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
