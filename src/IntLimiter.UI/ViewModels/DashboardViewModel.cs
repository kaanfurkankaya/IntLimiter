using CommunityToolkit.Mvvm.ComponentModel;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using System;
using System.Threading;

namespace IntLimiter.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ITrafficMonitor _monitor;
    private readonly SynchronizationContext? _syncContext;
    public Action<Action>? UiDispatch { get; set; }

    [ObservableProperty]
    private string _totalDownload = "0.0 MB/s";

    [ObservableProperty]
    private string _totalUpload = "0.0 MB/s";

    [ObservableProperty]
    private string _monitoringStatus = "Monitoring has not started.";

    public DashboardViewModel(ITrafficMonitor monitor)
    {
        _monitor = monitor;
        _syncContext = SynchronizationContext.Current;
        _monitor.TrafficUpdated += OnTrafficUpdated;
        Refresh();
    }

    private void OnTrafficUpdated(object? sender, EventArgs e)
    {
        RunOnUiThread(Refresh);
    }

    public void RefreshFromMonitor()
    {
        Refresh();
    }

    private void Refresh()
    {
        var down = NetworkUnitConverter.ConvertFromBytesPerSecond(_monitor.GlobalDownloadBytesPerSecond, NetworkUnit.MegabytesPerSecond);
        var up = NetworkUnitConverter.ConvertFromBytesPerSecond(_monitor.GlobalUploadBytesPerSecond, NetworkUnit.MegabytesPerSecond);

        TotalDownload = $"{down:0.00} MB/s";
        TotalUpload = $"{up:0.00} MB/s";
        MonitoringStatus = _monitor.StatusMessage;
    }

    private void RunOnUiThread(Action action)
    {
        if (UiDispatch != null)
        {
            UiDispatch(action);
            return;
        }

        if (_syncContext != null)
        {
            _syncContext.Post(_ => action(), null);
            return;
        }

        action();
    }
}
