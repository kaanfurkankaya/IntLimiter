using CommunityToolkit.Mvvm.ComponentModel;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace IntLimiter.UI.ViewModels;

public partial class ProcessesViewModel : ObservableObject
{
    private readonly ITrafficMonitor _monitor;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<ProcessNetworkUsage> Processes { get; } = new();

    public ProcessesViewModel(ITrafficMonitor monitor)
    {
        _monitor = monitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _monitor.TrafficUpdated += OnTrafficUpdated;
    }

    private void OnTrafficUpdated(object? sender, EventArgs e)
    {
        var active = _monitor.GetActiveProcesses()
            .Where(p => p.DownloadBytesPerSecond > 0 || p.UploadBytesPerSecond > 0)
            .OrderByDescending(p => p.DownloadBytesPerSecond + p.UploadBytesPerSecond)
            .ToList();

        _dispatcherQueue.TryEnqueue(() =>
        {
            Processes.Clear();
            foreach (var p in active)
            {
                Processes.Add(p);
            }
        });
    }
}
