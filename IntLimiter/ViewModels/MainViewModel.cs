using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntLimiter.Models;
using IntLimiter.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace IntLimiter.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly NetworkMonitorService _monitor;
    private readonly BandwidthLimiterService _limiter;

    [ObservableProperty]
    private ObservableCollection<ProcessNetworkInfo> _processes = new();

    [ObservableProperty]
    private ProcessNetworkInfo? _selectedProcess;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private long _totalDownloadSpeed;

    [ObservableProperty]
    private long _totalUploadSpeed;

    [ObservableProperty]
    private int _activeProcessCount;

    [ObservableProperty]
    private bool _showOnlyActive = true;

    [ObservableProperty]
    private bool _isGlobalLimitActive;

    [ObservableProperty]
    private string _globalLimitText = "";

    public MainViewModel(NetworkMonitorService monitor, BandwidthLimiterService limiter)
    {
        _monitor = monitor;
        _limiter = limiter;
        _monitor.ProcessListUpdated += OnProcessListUpdated;
    }

    private void OnProcessListUpdated()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            TotalDownloadSpeed = _monitor.TotalDownloadSpeed;
            TotalUploadSpeed = _monitor.TotalUploadSpeed;

            var snapshot = _monitor.ProcessMap.Values.ToList();

            // Build a set of active PIDs for O(1) lookups
            var activePids = new HashSet<int>(snapshot.Select(p => p.ProcessId));

            // Remove dead processes
            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                if (!activePids.Contains(Processes[i].ProcessId))
                    Processes.RemoveAt(i);
            }

            // Add/update visible processes
            var existingPids = new HashSet<int>(Processes.Select(p => p.ProcessId));
            foreach (var proc in snapshot)
            {
                if (!existingPids.Contains(proc.ProcessId) && ShouldShow(proc))
                    Processes.Add(proc);
                else if (existingPids.Contains(proc.ProcessId) && !ShouldShow(proc))
                {
                    var idx = -1;
                    for (int i = 0; i < Processes.Count; i++)
                        if (Processes[i].ProcessId == proc.ProcessId) { idx = i; break; }
                    if (idx >= 0) Processes.RemoveAt(idx);
                }
            }

            ActiveProcessCount = Processes.Count;
        });
    }

    private bool ShouldShow(ProcessNetworkInfo proc)
    {
        if (!string.IsNullOrEmpty(SearchText))
        {
            if (!proc.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
                !proc.ProcessId.ToString().Contains(SearchText))
                return false;
        }
        if (ShowOnlyActive)
        {
            if (proc.DownloadSpeed == 0 && proc.UploadSpeed == 0 &&
                proc.TotalDownloaded == 0 && proc.TotalUploaded == 0)
                return false;
        }
        return true;
    }

    partial void OnSearchTextChanged(string value) => RefreshList();
    partial void OnShowOnlyActiveChanged(bool value) => RefreshList();

    private void RefreshList()
    {
        var snapshot = _monitor.ProcessMap.Values.ToList();
        Processes.Clear();
        foreach (var proc in snapshot)
            if (ShouldShow(proc))
                Processes.Add(proc);
        ActiveProcessCount = Processes.Count;
    }

    [RelayCommand]
    private async Task SetLimit()
    {
        if (SelectedProcess == null) return;

        var dialog = new Views.LimitDialog(
            SelectedProcess.ProcessName,
            SelectedProcess.DownloadLimit,
            SelectedProcess.UploadLimit,
            isGlobal: false);
        dialog.Owner = System.Windows.Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var downloadLimit = dialog.DownloadLimitBitsPerSecond;
            var uploadLimit = dialog.UploadLimitBitsPerSecond;

            SelectedProcess.DownloadLimit = downloadLimit;
            SelectedProcess.UploadLimit = uploadLimit;

            var effectiveLimit = Math.Max(downloadLimit, uploadLimit);
            if (effectiveLimit > 0)
            {
                var success = await _limiter.SetLimitAsync(SelectedProcess.ProcessName, effectiveLimit);
                SelectedProcess.IsLimited = success;
            }
            else
            {
                await _limiter.RemoveLimitAsync(SelectedProcess.ProcessName);
                SelectedProcess.IsLimited = false;
                SelectedProcess.DownloadLimit = 0;
                SelectedProcess.UploadLimit = 0;
            }
        }
    }

    [RelayCommand]
    private async Task RemoveLimit()
    {
        if (SelectedProcess == null) return;
        await _limiter.RemoveLimitAsync(SelectedProcess.ProcessName);
        SelectedProcess.DownloadLimit = 0;
        SelectedProcess.UploadLimit = 0;
        SelectedProcess.IsLimited = false;
    }

    [RelayCommand]
    private async Task SetGlobalLimit()
    {
        var dialog = new Views.LimitDialog("Tüm PC Trafiği", 0, 0, isGlobal: true);
        dialog.Owner = System.Windows.Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var limit = Math.Max(dialog.DownloadLimitBitsPerSecond, dialog.UploadLimitBitsPerSecond);
            if (limit > 0)
            {
                var success = await _limiter.SetGlobalLimitAsync(limit);
                IsGlobalLimitActive = success;
                GlobalLimitText = success ? FormatBits(limit) : "Hata!";
            }
            else
            {
                await _limiter.RemoveGlobalLimitAsync();
                IsGlobalLimitActive = false;
                GlobalLimitText = "";
            }
        }
    }

    [RelayCommand]
    private async Task RemoveGlobalLimit()
    {
        await _limiter.RemoveGlobalLimitAsync();
        IsGlobalLimitActive = false;
        GlobalLimitText = "";
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        if (SelectedProcess == null || string.IsNullOrEmpty(SelectedProcess.FilePath)) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedProcess.FilePath}\""); }
        catch { }
    }

    private static string FormatBits(long bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:F1} Mbit/s",
        >= 1_000 => $"{bitsPerSecond / 1_000.0:F0} Kbit/s",
        _ => $"{bitsPerSecond} bit/s"
    };

    public void StartMonitoring() => _monitor.Start();

    public async Task StopMonitoring()
    {
        _monitor.Stop();
        await _limiter.CleanupAllAsync();
    }
}
