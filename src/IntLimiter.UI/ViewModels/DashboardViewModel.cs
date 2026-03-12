using CommunityToolkit.Mvvm.ComponentModel;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using Microsoft.UI.Dispatching;
using System;

namespace IntLimiter.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ITrafficMonitor _monitor;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty]
    private string _totalDownload = "0.0 MB/s";

    [ObservableProperty]
    private string _totalUpload = "0.0 MB/s";

    public DashboardViewModel(ITrafficMonitor monitor)
    {
        _monitor = monitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); // Captured from UI Thread
        _monitor.TrafficUpdated += OnTrafficUpdated;
    }

    private void OnTrafficUpdated(object? sender, EventArgs e)
    {
        var down = NetworkUnitConverter.ConvertFromBytesPerSecond(_monitor.GlobalDownloadBytesPerSecond, NetworkUnit.MegabytesPerSecond);
        var up = NetworkUnitConverter.ConvertFromBytesPerSecond(_monitor.GlobalUploadBytesPerSecond, NetworkUnit.MegabytesPerSecond);

        _dispatcherQueue.TryEnqueue(() =>
        {
            TotalDownload = $"{down:0.00} MB/s";
            TotalUpload = $"{up:0.00} MB/s";
        });
    }
}
