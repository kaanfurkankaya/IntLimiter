using CommunityToolkit.Mvvm.ComponentModel;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace IntLimiter.UI.ViewModels;

public partial class ProcessesViewModel : ObservableObject
{
    private readonly ITrafficMonitor _monitor;
    private readonly SynchronizationContext? _syncContext;
    public Action<Action>? UiDispatch { get; set; }

    public ObservableCollection<ProcessNetworkUsage> Processes { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _monitoringStatus = "Monitoring has not started.";

    [ObservableProperty]
    private string _emptyStateMessage = "No process traffic is available yet.";

    public ProcessesViewModel(ITrafficMonitor monitor)
    {
        _monitor = monitor;
        _syncContext = SynchronizationContext.Current;
        _monitor.TrafficUpdated += OnTrafficUpdated;
        Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
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
        var processes = _monitor.GetActiveProcesses()
            .Where(p => string.IsNullOrWhiteSpace(SearchText)
                || p.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || p.ProcessId.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(p.ExecutablePath) && p.ExecutablePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => p.DownloadBytesPerSecond + p.UploadBytesPerSecond)
            .ThenByDescending(p => p.LastActivityUtc)
            .ThenBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Processes.Clear();
        foreach (var process in processes)
        {
            Processes.Add(process);
        }

        MonitoringStatus = _monitor.StatusMessage;
        EmptyStateMessage = Processes.Count > 0
            ? string.Empty
            : _monitor.IsRunning
                ? "Monitoring is active, but no running processes matched the current filter."
                : _monitor.StatusMessage;
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
