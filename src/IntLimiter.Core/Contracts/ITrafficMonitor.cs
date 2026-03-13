using IntLimiter.Core.Models;
using System;
using System.Collections.Generic;

namespace IntLimiter.Core.Contracts;

public interface ITrafficMonitor : IDisposable
{
    void Start();
    void Stop();
    
    bool IsRunning { get; }
    bool RequiresAdministrator { get; }
    string StatusMessage { get; }

    long GlobalDownloadBytesPerSecond { get; }
    long GlobalUploadBytesPerSecond { get; }
    
    IEnumerable<ProcessNetworkUsage> GetActiveProcesses();
    
    event EventHandler? TrafficUpdated;
}
