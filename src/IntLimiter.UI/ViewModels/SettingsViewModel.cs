using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntLimiter.Updater;
using System.Threading.Tasks;

namespace IntLimiter.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IUpdateEngine _updateEngine;

    [ObservableProperty]
    private string _currentVersion;

    [ObservableProperty]
    private string _updateStatus = "Check for Updates";

    [ObservableProperty]
    private bool _isUpdateAvailable;

    private Velopack.UpdateInfo? _pendingUpdate;

    public SettingsViewModel(IUpdateEngine updateEngine)
    {
        _updateEngine = updateEngine;
        _currentVersion = $"Version: {_updateEngine.CurrentVersion}";
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        UpdateStatus = "Checking...";
        _pendingUpdate = await _updateEngine.CheckForUpdatesAsync();
        
        if (_pendingUpdate != null)
        {
            UpdateStatus = $"Update to {_pendingUpdate.TargetFullRelease.Version}";
            IsUpdateAvailable = true;
        }
        else
        {
            UpdateStatus = "You are up to date";
            IsUpdateAvailable = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdate()
    {
        if (_pendingUpdate != null)
        {
            UpdateStatus = "Downloading...";
            await _updateEngine.DownloadAndApplyUpdateAsync(_pendingUpdate);
        }
    }
}
