using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Infrastructure;
using IntLimiter.Core.Ipc;
using IntLimiter.Core.Models;

namespace IntLimiter.Client;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IServiceControlClient _serviceClient = new NamedPipeServiceControlClient();
    private readonly Dictionary<string, string> _friendlyNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refreshTimer;
    private readonly string _settingsPath;
    private ClientSettings _settings;
    private IReadOnlyList<ProcessIdentity> _lastProcesses = [];
    private ProcessSortMode _processSortMode = ProcessSortMode.TotalTrafficDescending;
    private RateUnit _rateUnit = RateUnit.KilobytesPerSecond;
    private UiLanguage _language = UiLanguage.Turkish;
    private UiTheme _theme = UiTheme.System;
    private ProcessIdentity? _selectedProcess;
    private ServiceDiagnosticsDto? _lastDiagnostics;
    private Process? _bundledServiceProcess;
    private bool _refreshing;
    private bool _applyingSettings;

    public MainWindow()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IntLimiter",
            "client-settings.json");
        _settings = LoadSettings();
        _language = _settings.Language;
        _theme = _settings.Theme;
        _rateUnit = _settings.RateUnit;

        InitializeComponent();
        DataContext = this;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_settings.RefreshIntervalMs)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshStateAsync();

        _applyingSettings = true;
        RateUnitBox.SelectedIndex = RateUnitToIndex(_rateUnit);
        _applyingSettings = false;

        ApplyTheme();
        ApplyLocalization();
        UpdateMenuChecks();
        UpdateProcessSortHeaders();
    }

    public ObservableCollection<ProcessIdentity> Processes { get; } = [];
    public ObservableCollection<ProcessRow> ProcessRows { get; } = [];
    public ObservableCollection<BandwidthRule> Rules { get; } = [];
    public ObservableCollection<LogEntry> Logs { get; } = [];

    public ProcessIdentity? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            _selectedProcess = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ShowProcessPlaceholder();
        await EnsureBundledServiceStartedAsync();

        if (_settings.AutoRefresh)
        {
            _refreshTimer.Start();
        }

        await RefreshStateAsync();
    }

    private async void RefreshProcesses_Click(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();
    private void ProcessSearchText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RebuildProcessRows();
    private void ProcessFilter_Changed(object sender, RoutedEventArgs e) => RebuildProcessRows();

    private void NameHeader_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _processSortMode = ProcessSortMode.NameAscending;
        RebuildProcessRows();
    }

    private void UploadHeader_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _processSortMode = ProcessSortMode.UploadDescending;
        RebuildProcessRows();
    }

    private void DownloadHeader_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _processSortMode = ProcessSortMode.DownloadDescending;
        RebuildProcessRows();
    }

    private void RateUnitBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _rateUnit = RateUnitBox.SelectedIndex switch
        {
            0 => RateUnit.BytesPerSecond,
            2 => RateUnit.MegabytesPerSecond,
            3 => RateUnit.KilobitsPerSecond,
            4 => RateUnit.MegabitsPerSecond,
            _ => RateUnit.KilobytesPerSecond
        };
        if (!_applyingSettings)
        {
            _settings.RateUnit = _rateUnit;
            SaveSettings();
        }

        RebuildProcessRows();
    }

    private void ProcessTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ProcessRow row)
        {
            SelectedProcess = row.Identity;
        }
    }

    private async void StartService_Click(object sender, RoutedEventArgs e) => await ControlWindowsServiceAsync(start: true);
    private async void StopService_Click(object sender, RoutedEventArgs e) => await ControlWindowsServiceAsync(start: false);
    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => await RefreshDiagnosticsAsync(showErrors: true);
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenLogFolder();
    private void CopyDiagnosticReport_Click(object sender, RoutedEventArgs e) => CopyDiagnosticReport();
    private async void ApplyRules_Click(object sender, RoutedEventArgs e) => await ApplyCurrentRulesAsync();
    private async void ApplyGlobalRule_Click(object sender, RoutedEventArgs e) => await AddGlobalRulesAsync();
    private async void ApplyProcessRule_Click(object sender, RoutedEventArgs e) => await AddProcessRulesAsync();
    private async void DeleteRule_Click(object sender, RoutedEventArgs e) => await DeleteSelectedRuleAsync();
    private async void StopAll_Click(object sender, RoutedEventArgs e) => await StopAllAsync();
    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();
    private void TurkishMenuItem_Click(object sender, RoutedEventArgs e) => SetLanguage(UiLanguage.Turkish);
    private void EnglishMenuItem_Click(object sender, RoutedEventArgs e) => SetLanguage(UiLanguage.English);
    private void SystemThemeMenuItem_Click(object sender, RoutedEventArgs e) => SetTheme(UiTheme.System);
    private void LightThemeMenuItem_Click(object sender, RoutedEventArgs e) => SetTheme(UiTheme.Light);
    private void DarkThemeMenuItem_Click(object sender, RoutedEventArgs e) => SetTheme(UiTheme.Dark);
    private void AutoRefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoRefresh = AutoRefreshMenuItem.IsChecked;
        if (_settings.AutoRefresh)
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }

        SaveSettings();
        UpdateMenuChecks();
    }

    private void RefreshIntervalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.RefreshIntervalMs = sender switch
        {
            var item when ReferenceEquals(item, Refresh1sMenuItem) => 1000,
            var item when ReferenceEquals(item, Refresh5sMenuItem) => 5000,
            _ => 3000
        };
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(_settings.RefreshIntervalMs);
        SaveSettings();
        UpdateMenuChecks();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, L("AboutMessage"), L("AboutTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowProcessPlaceholder()
    {
        if (ProcessRows.Count > 0)
        {
            return;
        }

        ProcessRows.Add(new ProcessRow
        {
            Key = "loading",
            Name = L("LoadingProcesses"),
            SortName = L("LoadingProcesses"),
            PidText = "",
            UploadText = "",
            DownloadText = "",
            Path = ""
        });
    }

    private async Task EnsureBundledServiceStartedAsync()
    {
        if (await CanReachServiceAsync())
        {
            return;
        }

        if (!Privilege.IsAdministrator())
        {
            ServiceStatusText.Text = L("Unavailable");
            AdminStatusText.Text = L("NotElevated");
            ModeStatusText.Text = L("RunSetupOnce");
            return;
        }

        var serviceExe = Path.Combine(AppContext.BaseDirectory, "IntLimiter.Service.exe");
        if (!File.Exists(serviceExe))
        {
            return;
        }

        ModeStatusText.Text = L("StartingService");
        try
        {
            if (!IsProcessRunningFromPath("IntLimiter.Service", serviceExe))
            {
                _bundledServiceProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = serviceExe,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }

            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(250);
                if (await CanReachServiceAsync())
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ModeStatusText.Text = ex.Message;
        }
    }

    private async Task<bool> CanReachServiceAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            _ = await _serviceClient.GetStateAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessRunningFromPath(string processName, string expectedPath)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                if (!process.HasExited)
                {
                    return true;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private async Task RefreshStateAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var state = await _serviceClient.GetStateAsync(CancellationToken.None);
            ServiceStatusText.Text = state.Runtime.IsRunning ? L("Running") : L("Stopped");
            AdminStatusText.Text = state.Runtime.IsAdmin ? L("Elevated") : L("NotElevated");
            ModeStatusText.Text = FormatLimiterMode(state.Runtime.Mode, state.Runtime.IsRunning);
            QueueStatusText.Text = $"{state.Runtime.QueuedPacketCount:N0} {L("Packets")} / {FormatBytes(state.Runtime.QueuedBytes)}";
            ReplaceCollection(Rules, state.Rules);
            ReplaceCollection(Logs, FilterDisplayLogs(state.Logs).TakeLast(20));
            UpdateDiagnosticsFromRuntime(state.Runtime);
            await RefreshProcessesAsync(showErrors: false);
        }
        catch (Exception ex)
        {
            ServiceStatusText.Text = L("Unavailable");
            ModeStatusText.Text = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshProcessesAsync(bool showErrors = true)
    {
        try
        {
            var selected = SelectedProcess;
            var processes = await _serviceClient.GetProcessesAsync(CancellationToken.None);
            _lastProcesses = processes;
            ReplaceCollection(Processes, processes);
            RebuildProcessRows();
            SelectedProcess = FindUpdatedSelection(processes, selected);
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                MessageBox.Show(this, ex.Message, L("ProcessRefreshFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async Task RefreshDiagnosticsAsync(bool showErrors)
    {
        try
        {
            _lastDiagnostics = await _serviceClient.GetDiagnosticsAsync(CancellationToken.None);
            RenderDiagnostics(_lastDiagnostics);
        }
        catch (Exception ex)
        {
            DiagRuntimeModeText.Text = L("Unavailable");
            DiagLastErrorText.Text = ex.Message;
            if (showErrors)
            {
                MessageBox.Show(this, ex.Message, L("RefreshDiagnosticsFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void UpdateDiagnosticsFromRuntime(LimiterRuntimeStatus runtime)
    {
        _lastDiagnostics = new ServiceDiagnosticsDto
        {
            RuntimeMode = runtime.Mode,
            IsRunning = runtime.IsRunning,
            IsAdmin = runtime.IsAdmin,
            WinDivertLoaded = runtime.WinDivertReady,
            QosFallbackActive = runtime.Mode == LimiterMode.QosPolicyFallback && runtime.IsRunning,
            ActiveRuleCount = runtime.ActiveRuleCount,
            QueueLength = runtime.QueuedPacketCount,
            QueuedBytes = runtime.QueuedBytes,
            CapturedPackets = runtime.CapturedPackets,
            DelayedPackets = runtime.DelayedPackets,
            ReinjectedPackets = runtime.ReinjectedPackets,
            DroppedPackets = runtime.DroppedPackets,
            ProcessMappingSuccess = runtime.ProcessMappingSuccess,
            ProcessMappingFailed = runtime.ProcessMappingFailed,
            LastError = runtime.LastError,
            Message = runtime.Message,
            UpdatedAt = runtime.UpdatedAt
        };
        RenderDiagnostics(_lastDiagnostics);
    }

    private void RenderDiagnostics(ServiceDiagnosticsDto diagnostics)
    {
        DiagRuntimeModeText.Text = FormatLimiterMode(diagnostics.RuntimeMode, diagnostics.IsRunning);
        DiagCapturedText.Text = diagnostics.CapturedPackets.ToString("N0");
        DiagDelayedText.Text = diagnostics.DelayedPackets.ToString("N0");
        DiagReinjectedText.Text = diagnostics.ReinjectedPackets.ToString("N0");
        DiagDroppedText.Text = diagnostics.DroppedPackets.ToString("N0");
        DiagQueueText.Text = $"{diagnostics.QueueLength:N0} / {FormatBytes(diagnostics.QueuedBytes)}";
        DiagMappingText.Text = $"{diagnostics.ProcessMappingSuccess:N0} / {diagnostics.ProcessMappingFailed:N0}";
        DiagLastErrorText.Text = string.IsNullOrWhiteSpace(diagnostics.LastError)
            ? diagnostics.Message
            : diagnostics.LastError;
    }

    private static void OpenLogFolder()
    {
        ApplicationPaths.EnsureProgramData();
        Process.Start(new ProcessStartInfo
        {
            FileName = ApplicationPaths.ProgramDataDirectory,
            UseShellExecute = true
        });
    }

    private void CopyDiagnosticReport()
    {
        if (_lastDiagnostics is null)
        {
            Clipboard.SetText(L("NoDiagnostics"));
            return;
        }

        var report = string.Join(Environment.NewLine, new[]
        {
            $"{L("RuntimeMode")}: {_lastDiagnostics.RuntimeMode}",
            $"{L("IsRunning")}: {_lastDiagnostics.IsRunning}",
            $"WinDivert: {_lastDiagnostics.WinDivertLoaded}",
            $"QoS: {_lastDiagnostics.QosFallbackActive}",
            $"{L("CapturedPackets")}: {_lastDiagnostics.CapturedPackets}",
            $"{L("DelayedPackets")}: {_lastDiagnostics.DelayedPackets}",
            $"{L("ReinjectedPackets")}: {_lastDiagnostics.ReinjectedPackets}",
            $"{L("DroppedPackets")}: {_lastDiagnostics.DroppedPackets}",
            $"{L("QueueLength")}: {_lastDiagnostics.QueueLength}",
            $"{L("MappingOkFail")}: {_lastDiagnostics.ProcessMappingSuccess}/{_lastDiagnostics.ProcessMappingFailed}",
            $"{L("ActiveRules")}: {_lastDiagnostics.ActiveRuleCount}",
            $"{L("LastError")}: {_lastDiagnostics.LastError}",
            $"{L("Message")}: {_lastDiagnostics.Message}",
            $"{L("Uptime")}: {_lastDiagnostics.ServiceUptime}",
            $"{L("LogPath")}: {_lastDiagnostics.LogPath}"
        });
        Clipboard.SetText(report);
    }

    private async Task ControlWindowsServiceAsync(bool start)
    {
        try
        {
            await Task.Run(() =>
            {
                using var service = new ServiceController("IntLimiter.Service");
                if (start)
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                }
                else if (service.Status != ServiceControllerStatus.Stopped)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
            });

            await RefreshStateAsync();
        }
        catch (InvalidOperationException) when (start)
        {
            await EnsureBundledServiceStartedAsync();
            await RefreshStateAsync();
        }
        catch (InvalidOperationException) when (!start)
        {
            await StopBundledServiceAsync();
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("WindowsServiceControlFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StopBundledServiceAsync()
    {
        try
        {
            await _serviceClient.StopAllAsync(CancellationToken.None);
        }
        catch
        {
            // The process may already be gone or the installed service may not be available.
        }

        var serviceExe = Path.Combine(AppContext.BaseDirectory, "IntLimiter.Service.exe");
        var bundledProcessId = _bundledServiceProcess?.Id;
        foreach (var process in Process.GetProcessesByName("IntLimiter.Service"))
        {
            try
            {
                var samePath = string.Equals(process.MainModule?.FileName, serviceExe, StringComparison.OrdinalIgnoreCase);
                if (samePath || process.Id == bundledProcessId)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch
            {
                // Ignore best-effort shutdown failures; diagnostics will show the remaining state.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task AddGlobalRulesAsync()
    {
        var rules = Rules.Where(rule => rule.Scope != RuleScopeKind.Global).ToList();
        var enabled = GlobalRuleEnabledBox.IsChecked == true;

        if (TryReadLimit(GlobalDownloadLimitText.Text, GlobalDownloadUnitBox.SelectedIndex, out var downloadLimit))
        {
            rules.Add(CreateRule(L("GlobalDownloadRuleName"), RuleScopeKind.Global, TrafficDirection.Download, downloadLimit, enabled));
        }

        if (TryReadLimit(GlobalUploadLimitText.Text, GlobalUploadUnitBox.SelectedIndex, out var uploadLimit))
        {
            rules.Add(CreateRule(L("GlobalUploadRuleName"), RuleScopeKind.Global, TrafficDirection.Upload, uploadLimit, enabled));
        }

        await SaveRulesAsync(rules);
    }

    private async Task AddProcessRulesAsync()
    {
        if (SelectedProcess is null)
        {
            MessageBox.Show(this, L("SelectProcessFirst"), "IntLimiter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = SelectedProcess;
        var rules = Rules.Where(rule => !IsForProcess(rule, selected)).ToList();
        var scope = !string.IsNullOrWhiteSpace(selected.ExecutablePath)
            ? RuleScopeKind.ProcessPath
            : RuleScopeKind.ProcessName;
        var enabled = ProcessRuleEnabledBox.IsChecked == true;

        if (TryReadLimit(ProcessDownloadLimitText.Text, ProcessDownloadUnitBox.SelectedIndex, out var downloadLimit))
        {
            rules.Add(CreateProcessRule(selected, scope, TrafficDirection.Download, downloadLimit, enabled));
        }

        if (TryReadLimit(ProcessUploadLimitText.Text, ProcessUploadUnitBox.SelectedIndex, out var uploadLimit))
        {
            rules.Add(CreateProcessRule(selected, scope, TrafficDirection.Upload, uploadLimit, enabled));
        }

        await SaveRulesAsync(rules);
    }

    private async Task ApplyCurrentRulesAsync()
    {
        var rules = Rules.ToList();
        AppendGlobalRulesFromInputs(rules);
        await SaveRulesAsync(rules);
    }

    private async Task DeleteSelectedRuleAsync()
    {
        if (RulesGrid.SelectedItem is not BandwidthRule rule)
        {
            return;
        }

        try
        {
            await _serviceClient.DeleteRuleAsync(rule.RuleId, CancellationToken.None);
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("DeleteRuleFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StopAllAsync()
    {
        try
        {
            await _serviceClient.StopAllAsync(CancellationToken.None);
            GlobalDownloadLimitText.Text = "";
            GlobalUploadLimitText.Text = "";
            ProcessDownloadLimitText.Text = "";
            ProcessUploadLimitText.Text = "";
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("StopAllFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SaveRulesAsync(IReadOnlyList<BandwidthRule> rules)
    {
        try
        {
            await _serviceClient.ApplyRulesAsync(rules, CancellationToken.None);
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("ApplyRulesFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AppendGlobalRulesFromInputs(List<BandwidthRule> rules)
    {
        var enabled = GlobalRuleEnabledBox.IsChecked == true;
        var hasInput = false;

        if (TryReadLimit(GlobalDownloadLimitText.Text, GlobalDownloadUnitBox.SelectedIndex, out var downloadLimit))
        {
            hasInput = true;
            rules.RemoveAll(rule => rule.Scope == RuleScopeKind.Global && rule.Direction == TrafficDirection.Download);
            rules.Add(CreateRule(L("GlobalDownloadRuleName"), RuleScopeKind.Global, TrafficDirection.Download, downloadLimit, enabled));
        }

        if (TryReadLimit(GlobalUploadLimitText.Text, GlobalUploadUnitBox.SelectedIndex, out var uploadLimit))
        {
            hasInput = true;
            rules.RemoveAll(rule => rule.Scope == RuleScopeKind.Global && rule.Direction == TrafficDirection.Upload);
            rules.Add(CreateRule(L("GlobalUploadRuleName"), RuleScopeKind.Global, TrafficDirection.Upload, uploadLimit, enabled));
        }

        if (!hasInput)
        {
            return;
        }
    }

    private static BandwidthRule CreateRule(
        string name,
        RuleScopeKind scope,
        TrafficDirection direction,
        long bytesPerSecond,
        bool enabled)
    {
        return new BandwidthRule
        {
            Name = name,
            Scope = scope,
            Direction = direction,
            LimitBytesPerSecond = bytesPerSecond,
            Enabled = enabled
        };
    }

    private static BandwidthRule CreateProcessRule(
        ProcessIdentity process,
        RuleScopeKind scope,
        TrafficDirection direction,
        long bytesPerSecond,
        bool enabled)
    {
        return new BandwidthRule
        {
            Name = $"{process.ProcessName} {direction}",
            Scope = scope,
            Direction = direction,
            LimitBytesPerSecond = bytesPerSecond,
            Enabled = enabled,
            ProcessId = process.ProcessId,
            ProcessName = process.ProcessName,
            ProcessPath = process.ExecutablePath
        };
    }

    private static bool IsForProcess(BandwidthRule rule, ProcessIdentity process)
    {
        if (rule.Scope == RuleScopeKind.ProcessPath && !string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return string.Equals(rule.ProcessPath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }

        if (rule.Scope == RuleScopeKind.ProcessName)
        {
            return string.Equals(rule.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        return rule.Scope == RuleScopeKind.Pid && rule.ProcessId == process.ProcessId;
    }

    private static bool TryReadLimit(string text, int unitIndex, out long bytesPerSecond)
    {
        bytesPerSecond = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        var multiplier = unitIndex == 1 ? 1024d * 1024d : 1024d;
        bytesPerSecond = (long)(value * multiplier);
        return bytesPerSecond > 0;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var item in values)
        {
            collection.Add(item);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static IEnumerable<LogEntry> FilterDisplayLogs(IEnumerable<LogEntry> logs)
    {
        var noisyEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PacketCaptured",
            "PacketReinjected",
            "ProcessMappingSuccess",
            "ProcessMappingFailed"
        };

        return logs.Where(log => !noisyEvents.Contains(log.Event));
    }

    private IReadOnlyList<ProcessRow> BuildProcessRows(
        IReadOnlyList<ProcessIdentity> processes,
        IEnumerable<ProcessRow> currentRows)
    {
        var expandedKeys = currentRows.Where(row => row.IsExpanded).Select(row => row.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = processes
            .GroupBy(GetProcessGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateProcessRow(group.Key, group.ToArray(), expandedKeys))
            .ToArray();

        return SortProcessRows(rows).ToArray();
    }

    private ProcessRow CreateProcessRow(string key, ProcessIdentity[] group, ISet<string> expandedKeys)
    {
        var representative = group
            .OrderByDescending(process => process.UploadBytesPerSecond + process.DownloadBytesPerSecond)
            .ThenBy(process => process.ProcessId)
            .First();
        var upload = group.Sum(process => process.UploadBytesPerSecond);
        var download = group.Sum(process => process.DownloadBytesPerSecond);
        var path = representative.ExecutablePath;
        var friendlyName = GetFriendlyProcessName(representative);

        if (group.Length == 1)
        {
            return new ProcessRow
            {
                Key = key,
                Name = friendlyName,
                SortName = friendlyName,
                PidText = representative.ProcessId.ToString(),
                Path = path ?? "",
                UploadBytesPerSecond = upload,
                DownloadBytesPerSecond = download,
                UploadText = FormatRate(upload, _rateUnit),
                DownloadText = FormatRate(download, _rateUnit),
                Identity = representative
            };
        }

        var aggregateIdentity = representative with
        {
            ProcessId = 0,
            UploadBytesPerSecond = upload,
            DownloadBytesPerSecond = download
        };
        return new ProcessRow
        {
            Key = key,
            Name = $"{friendlyName} ({group.Length})",
            SortName = friendlyName,
            PidText = $"{group.Length} {L("ProcessesShort")}",
            Path = path ?? "",
            UploadBytesPerSecond = upload,
            DownloadBytesPerSecond = download,
            UploadText = FormatRate(upload, _rateUnit),
            DownloadText = FormatRate(download, _rateUnit),
            Identity = aggregateIdentity,
            FontWeight = FontWeights.SemiBold,
            IsExpanded = expandedKeys.Contains(key),
            Children = new ObservableCollection<ProcessRow>(SortProcessIdentities(group)
                .Select(process => new ProcessRow
                {
                    Key = $"{key}|{process.ProcessId}",
                    Name = GetFriendlyProcessName(process),
                    SortName = GetFriendlyProcessName(process),
                    PidText = process.ProcessId.ToString(),
                    Path = process.ExecutablePath ?? "",
                    UploadBytesPerSecond = process.UploadBytesPerSecond,
                    DownloadBytesPerSecond = process.DownloadBytesPerSecond,
                    UploadText = FormatRate(process.UploadBytesPerSecond, _rateUnit),
                    DownloadText = FormatRate(process.DownloadBytesPerSecond, _rateUnit),
                    Identity = process
                })
                .ToArray())
        };
    }

    private ProcessIdentity? FindUpdatedSelection(IReadOnlyList<ProcessIdentity> processes, ProcessIdentity? selected)
    {
        if (selected is null)
        {
            return null;
        }

        if (selected.ProcessId > 0)
        {
            return processes.FirstOrDefault(process => process.ProcessId == selected.ProcessId) ?? selected;
        }

        if (!string.IsNullOrWhiteSpace(selected.ExecutablePath))
        {
            var group = processes
                .Where(process => string.Equals(process.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (group.Length > 0)
            {
                return selected with
                {
                    UploadBytesPerSecond = group.Sum(process => process.UploadBytesPerSecond),
                    DownloadBytesPerSecond = group.Sum(process => process.DownloadBytesPerSecond)
                };
            }
        }

        return selected;
    }

    private static string GetProcessGroupKey(ProcessIdentity process)
    {
        if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return "path:" + process.ExecutablePath;
        }

        return "name:" + process.ProcessName;
    }

    private string GetFriendlyProcessName(ProcessIdentity process)
    {
        var path = process.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return process.ProcessName;
        }

        if (_friendlyNameCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            var friendly = string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim();
            _friendlyNameCache[path] = friendly;
            return friendly;
        }
        catch
        {
            _friendlyNameCache[path] = process.ProcessName;
            return process.ProcessName;
        }
    }

    public sealed class ProcessRow : INotifyPropertyChanged
    {
        private string _key = "";
        private string _name = "";
        private string _sortName = "";
        private string _pidText = "";
        private string _path = "";
        private long _uploadBytesPerSecond;
        private long _downloadBytesPerSecond;
        private string _uploadText = "";
        private string _downloadText = "";
        private ProcessIdentity _identity = new();
        private FontWeight _fontWeight = FontWeights.Normal;
        private bool _isExpanded;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get => _key; set => SetField(ref _key, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public string SortName { get => _sortName; set => SetField(ref _sortName, value); }
        public string PidText { get => _pidText; set => SetField(ref _pidText, value); }
        public string Path { get => _path; set => SetField(ref _path, value); }
        public long UploadBytesPerSecond { get => _uploadBytesPerSecond; set => SetField(ref _uploadBytesPerSecond, value); }
        public long DownloadBytesPerSecond { get => _downloadBytesPerSecond; set => SetField(ref _downloadBytesPerSecond, value); }
        public string UploadText { get => _uploadText; set => SetField(ref _uploadText, value); }
        public string DownloadText { get => _downloadText; set => SetField(ref _downloadText, value); }
        public ProcessIdentity Identity { get => _identity; set => SetField(ref _identity, value); }
        public FontWeight FontWeight { get => _fontWeight; set => SetField(ref _fontWeight, value); }
        public bool IsExpanded { get => _isExpanded; set => SetField(ref _isExpanded, value); }
        public ObservableCollection<ProcessRow> Children { get; init; } = [];

        public void CopyFrom(ProcessRow source)
        {
            var expanded = IsExpanded;
            Key = source.Key;
            Name = source.Name;
            SortName = source.SortName;
            PidText = source.PidText;
            Path = source.Path;
            UploadBytesPerSecond = source.UploadBytesPerSecond;
            DownloadBytesPerSecond = source.DownloadBytesPerSecond;
            UploadText = source.UploadText;
            DownloadText = source.DownloadText;
            Identity = source.Identity;
            FontWeight = source.FontWeight;
            IsExpanded = expanded || source.IsExpanded;
            SyncProcessRows(Children, source.Children);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private void RebuildProcessRows()
    {
        if (ProcessRows is null)
        {
            return;
        }

        SyncProcessRows(ProcessRows, BuildProcessRows(GetVisibleProcesses(), ProcessRows));
        UpdateProcessSortHeaders();
    }

    private IReadOnlyList<ProcessIdentity> GetVisibleProcesses()
    {
        IEnumerable<ProcessIdentity> processes = _lastProcesses;
        if (ActiveOnlyBox?.IsChecked == true)
        {
            processes = processes.Where(process => process.UploadBytesPerSecond > 0 || process.DownloadBytesPerSecond > 0);
        }

        var query = ProcessSearchText?.Text;
        if (!string.IsNullOrWhiteSpace(query))
        {
            processes = processes.Where(process =>
                ContainsIgnoreCase(process.ProcessName, query) ||
                ContainsIgnoreCase(process.ExecutablePath, query) ||
                ContainsIgnoreCase(GetFriendlyProcessName(process), query));
        }

        return processes.ToArray();
    }

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void SyncProcessRows(ObservableCollection<ProcessRow> target, IReadOnlyList<ProcessRow> source)
    {
        if (target.Count != source.Count || target.Select(row => row.Key).Where(key => key != "loading").SequenceEqual(source.Select(row => row.Key)) is false)
        {
            target.Clear();
            foreach (var row in source)
            {
                target.Add(row);
            }

            return;
        }

        for (var i = 0; i < source.Count; i++)
        {
            target[i].CopyFrom(source[i]);
        }
    }

    private IEnumerable<ProcessRow> SortProcessRows(IEnumerable<ProcessRow> rows) =>
        _processSortMode switch
        {
            ProcessSortMode.NameAscending => rows.OrderBy(row => row.SortName, StringComparer.OrdinalIgnoreCase),
            ProcessSortMode.UploadDescending => rows.OrderByDescending(row => row.UploadBytesPerSecond).ThenBy(row => row.SortName, StringComparer.OrdinalIgnoreCase),
            ProcessSortMode.DownloadDescending => rows.OrderByDescending(row => row.DownloadBytesPerSecond).ThenBy(row => row.SortName, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(row => row.UploadBytesPerSecond + row.DownloadBytesPerSecond).ThenBy(row => row.SortName, StringComparer.OrdinalIgnoreCase)
        };

    private IEnumerable<ProcessIdentity> SortProcessIdentities(IEnumerable<ProcessIdentity> processes) =>
        _processSortMode switch
        {
            ProcessSortMode.NameAscending => processes.OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(process => process.ProcessId),
            ProcessSortMode.UploadDescending => processes.OrderByDescending(process => process.UploadBytesPerSecond).ThenBy(process => process.ProcessId),
            ProcessSortMode.DownloadDescending => processes.OrderByDescending(process => process.DownloadBytesPerSecond).ThenBy(process => process.ProcessId),
            _ => processes.OrderByDescending(process => process.UploadBytesPerSecond + process.DownloadBytesPerSecond).ThenBy(process => process.ProcessId)
        };

    private void UpdateProcessSortHeaders()
    {
        if (NameHeaderText is null || UploadHeaderText is null || DownloadHeaderText is null)
        {
            return;
        }

        NameHeaderText.Text = _processSortMode == ProcessSortMode.NameAscending ? $"{L("Name")} \u2191" : L("Name");
        UploadHeaderText.Text = _processSortMode == ProcessSortMode.UploadDescending ? $"{L("Upload")} \u2193" : L("Upload");
        DownloadHeaderText.Text = _processSortMode == ProcessSortMode.DownloadDescending ? $"{L("Download")} \u2193" : L("Download");
    }

    private ClientSettings LoadSettings()
    {
        var settings = new ClientSettings();
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<ClientSettings>(json, CreateJsonOptions()) ?? new ClientSettings();
            }
        }
        catch
        {
            settings = new ClientSettings();
        }

        NormalizeSettings(settings);
        return settings;
    }

    private void SaveSettings()
    {
        try
        {
            NormalizeSettings(_settings);
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, CreateJsonOptions()));
        }
        catch
        {
            // UI preferences should never block limiter usage.
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void NormalizeSettings(ClientSettings settings)
    {
        if (settings.RefreshIntervalMs < 1000)
        {
            settings.RefreshIntervalMs = 3000;
        }

        if (settings.RefreshIntervalMs != 1000 && settings.RefreshIntervalMs != 3000 && settings.RefreshIntervalMs != 5000)
        {
            settings.RefreshIntervalMs = 3000;
        }

        if (!Enum.IsDefined(settings.Language))
        {
            settings.Language = UiLanguage.Turkish;
        }

        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = UiTheme.System;
        }

        if (!Enum.IsDefined(settings.RateUnit))
        {
            settings.RateUnit = RateUnit.KilobytesPerSecond;
        }
    }

    private static int RateUnitToIndex(RateUnit unit) =>
        unit switch
        {
            RateUnit.BytesPerSecond => 0,
            RateUnit.MegabytesPerSecond => 2,
            RateUnit.KilobitsPerSecond => 3,
            RateUnit.MegabitsPerSecond => 4,
            _ => 1
        };

    private void SetLanguage(UiLanguage language)
    {
        _language = language;
        _settings.Language = language;
        ApplyLocalization();
        SaveSettings();
    }

    private void SetTheme(UiTheme theme)
    {
        _theme = theme;
        _settings.Theme = theme;
        ApplyTheme();
        UpdateMenuChecks();
        SaveSettings();
    }

    private void ApplyTheme()
    {
        var resolvedTheme = ResolveTheme(_theme);
        if (resolvedTheme == UiTheme.Dark)
        {
            SetBrush("WindowBackgroundBrush", 11, 16, 29);
            SetBrush("SurfaceBrush", 17, 24, 39);
            SetBrush("HeaderBackgroundBrush", 23, 34, 54);
            SetBrush("MenuBackgroundBrush", 9, 14, 24);
            SetBrush("InputBackgroundBrush", 13, 20, 34);
            SetBrush("PopupBackgroundBrush", 18, 27, 43);
            SetBrush("ButtonBackgroundBrush", 30, 41, 59);
            SetBrush("ButtonHoverBrush", 43, 57, 79);
            SetBrush("ButtonPressedBrush", 55, 72, 99);
            SetBrush("AlternateRowBrush", 16, 25, 41);
            SetBrush("SelectionBrush", 65, 80, 118);
            SetBrush("AccentBrush", 45, 212, 191);
            SetBrush("TextBrush", 236, 244, 255);
            SetBrush("MutedTextBrush", 148, 163, 184);
            SetBrush("BorderBrush", 51, 65, 85);
            ApplySystemBrushes();
            return;
        }

        SetBrush("WindowBackgroundBrush", 247, 250, 252);
        SetBrush("SurfaceBrush", 255, 255, 255);
        SetBrush("HeaderBackgroundBrush", 240, 246, 252);
        SetBrush("MenuBackgroundBrush", 255, 255, 255);
        SetBrush("InputBackgroundBrush", 255, 255, 255);
        SetBrush("PopupBackgroundBrush", 255, 255, 255);
        SetBrush("ButtonBackgroundBrush", 255, 255, 255);
        SetBrush("ButtonHoverBrush", 239, 249, 248);
        SetBrush("ButtonPressedBrush", 222, 242, 240);
        SetBrush("AlternateRowBrush", 248, 250, 252);
        SetBrush("SelectionBrush", 204, 251, 241);
        SetBrush("AccentBrush", 20, 184, 166);
        SetBrush("TextBrush", 17, 24, 39);
        SetBrush("MutedTextBrush", 71, 85, 105);
        SetBrush("BorderBrush", 203, 213, 225);
        ApplySystemBrushes();
    }

    private void SetBrush(string key, byte red, byte green, byte blue) =>
        Resources[key] = new SolidColorBrush(Color.FromRgb(red, green, blue));

    private void ApplySystemBrushes()
    {
        Resources[SystemColors.HighlightBrushKey] = Resources["SelectionBrush"];
        Resources[SystemColors.HighlightTextBrushKey] = Resources["TextBrush"];
        Resources[SystemColors.MenuHighlightBrushKey] = Resources["SelectionBrush"];
        Resources[SystemColors.MenuTextBrushKey] = Resources["TextBrush"];
        Resources[SystemColors.ControlBrushKey] = Resources["PopupBackgroundBrush"];
        Resources[SystemColors.ControlTextBrushKey] = Resources["TextBrush"];
        Resources[SystemColors.WindowBrushKey] = Resources["PopupBackgroundBrush"];
        Resources[SystemColors.WindowTextBrushKey] = Resources["TextBrush"];
    }

    private static UiTheme ResolveTheme(UiTheme theme)
    {
        if (theme != UiTheme.System)
        {
            return theme;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
            return appsUseLightTheme is int value && value == 0 ? UiTheme.Dark : UiTheme.Light;
        }
        catch
        {
            return UiTheme.Light;
        }
    }

    private void ApplyLocalization()
    {
        FileMenuItem.Header = L("File");
        OpenLogFolderMenuItem.Header = L("OpenLogFolder");
        ExitMenuItem.Header = L("Exit");
        ViewMenuItem.Header = L("View");
        LanguageMenuItem.Header = L("Language");
        TurkishMenuItem.Header = L("Turkish");
        EnglishMenuItem.Header = L("English");
        ThemeMenuItem.Header = L("Theme");
        SystemThemeMenuItem.Header = L("SystemTheme");
        LightThemeMenuItem.Header = L("LightTheme");
        DarkThemeMenuItem.Header = L("DarkTheme");
        AutoRefreshMenuItem.Header = L("AutoRefresh");
        RefreshIntervalMenuItem.Header = L("RefreshInterval");
        Refresh1sMenuItem.Header = L("Refresh1s");
        Refresh2sMenuItem.Header = L("Refresh2s");
        Refresh5sMenuItem.Header = L("Refresh5s");
        ToolsMenuItem.Header = L("Tools");
        RefreshProcessesMenuItem.Header = L("RefreshProcesses");
        ApplyRulesMenuItem.Header = L("ApplyRules");
        StopAllMenuItem.Header = L("StopAll");
        CopyDiagnosticMenuItem.Header = L("CopyDiagnostic");
        HelpMenuItem.Header = L("Help");
        AboutMenuItem.Header = L("About");

        ServiceLabelText.Text = $"{L("Service")}:";
        AdminLabelText.Text = $"{L("Admin")}:";
        AdminStatusText.Text = L("Checking");
        ModeLabelText.Text = $"{L("Mode")}:";
        QueueLabelText.Text = $"{L("Queue")}:";
        StartServiceButton.Content = L("StartService");
        StopServiceButton.Content = L("StopService");
        RefreshProcessesButton.Content = L("RefreshProcesses");
        ApplyRulesButton.Content = L("ApplyRules");
        DeleteRuleButton.Content = L("DeleteRule");
        StopAllButton.Content = L("StopAll");

        ProcessesGroupBox.Header = L("Processes");
        ProcessSearchLabelText.Text = L("Search");
        ActiveOnlyBox.Content = L("ActiveOnly");
        RateUnitLabelText.Text = L("RateUnit");
        PidHeaderText.Text = L("Pid");
        PathHeaderText.Text = L("Path");

        SelectedProcessGroupBox.Header = L("SelectedProcessLimit");
        ProcessDownloadLimitLabelText.Text = L("DownloadLimit");
        ProcessUploadLimitLabelText.Text = L("UploadLimit");
        ProcessRuleEnabledBox.Content = L("Enabled");
        ApplyProcessRuleButton.Content = L("AddSelectedProcessLimit");

        GlobalLimitsGroupBox.Header = L("GlobalLimits");
        GlobalDownloadLimitLabelText.Text = L("GlobalDownloadLimit");
        GlobalUploadLimitLabelText.Text = L("GlobalUploadLimit");
        GlobalRuleEnabledBox.Content = L("Enabled");
        ApplyGlobalRuleButton.Content = L("AddGlobalLimits");

        RulesGroupBox.Header = L("Rules");
        RuleEnabledColumn.Header = L("Enabled");
        RuleNameColumn.Header = L("Name");
        RuleScopeColumn.Header = L("Scope");
        RuleDirectionColumn.Header = L("Direction");
        RuleLimitColumn.Header = L("Limit");
        RuleProcessColumn.Header = L("Process");
        RulePathColumn.Header = L("Path");

        DiagnosticsGroupBox.Header = L("DiagnosticsTest");
        DiagRuntimeModeLabelText.Text = L("RuntimeMode");
        DiagCapturedLabelText.Text = L("CapturedPackets");
        DiagDelayedLabelText.Text = L("DelayedPackets");
        DiagReinjectedLabelText.Text = L("ReinjectedPackets");
        DiagDroppedLabelText.Text = L("DroppedPackets");
        DiagQueueLabelText.Text = L("QueueLength");
        DiagMappingLabelText.Text = L("MappingOkFail");
        DiagLastErrorLabelText.Text = L("LastError");
        RefreshDiagnosticsButton.Content = L("RefreshDiagnostics");
        OpenLogFolderButton.Content = L("OpenLogFolder");
        StopAllLimitsButton2.Content = L("StopAll");
        CopyDiagnosticReportButton.Content = L("CopyDiagnostic");
        LogsGroupBox.Header = L("LastLogs");

        UpdateMenuChecks();
        UpdateProcessSortHeaders();
        RebuildProcessRows();

        if (_lastDiagnostics is not null)
        {
            RenderDiagnostics(_lastDiagnostics);
        }
    }

    private void UpdateMenuChecks()
    {
        TurkishMenuItem.IsChecked = _language == UiLanguage.Turkish;
        EnglishMenuItem.IsChecked = _language == UiLanguage.English;
        SystemThemeMenuItem.IsChecked = _theme == UiTheme.System;
        LightThemeMenuItem.IsChecked = _theme == UiTheme.Light;
        DarkThemeMenuItem.IsChecked = _theme == UiTheme.Dark;
        AutoRefreshMenuItem.IsChecked = _settings.AutoRefresh;
        Refresh1sMenuItem.IsChecked = _settings.RefreshIntervalMs == 1000;
        Refresh2sMenuItem.IsChecked = _settings.RefreshIntervalMs == 3000;
        Refresh5sMenuItem.IsChecked = _settings.RefreshIntervalMs == 5000;
    }

    private string FormatLimiterMode(LimiterMode mode, bool isRunning)
    {
        if (!isRunning && mode != LimiterMode.Monitoring)
        {
            return L("Stopped");
        }

        return mode switch
        {
            LimiterMode.Monitoring => L("Monitoring"),
            LimiterMode.WinDivert => L("WinDivertActive"),
            LimiterMode.QosPolicyFallback => L("QosFallback"),
            LimiterMode.Error => L("DisabledError"),
            _ => L("Stopped")
        };
    }

    private string L(string key)
    {
        var source = _language == UiLanguage.English ? EnglishText : TurkishText;
        return source.TryGetValue(key, out var value) ? value : key;
    }
    private static string FormatRate(long bytesPerSecond, RateUnit unit)
    {
        return unit switch
        {
            RateUnit.BytesPerSecond => $"{bytesPerSecond:N0} B/s",
            RateUnit.MegabytesPerSecond => $"{bytesPerSecond / 1024d / 1024d:0.##} MB/s",
            RateUnit.KilobitsPerSecond => $"{bytesPerSecond * 8d / 1000d:0.#} Kbit/s",
            RateUnit.MegabitsPerSecond => $"{bytesPerSecond * 8d / 1_000_000d:0.##} Mbit/s",
            _ => $"{bytesPerSecond / 1024d:0.#} KB/s"
        };
    }

    private static readonly IReadOnlyDictionary<string, string> TurkishText = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["File"] = "Dosya",
        ["OpenLogFolder"] = "Log klasörünü aç",
        ["Exit"] = "Çıkış",
        ["View"] = "Görünüm",
        ["Language"] = "Dil",
        ["Turkish"] = "Türkçe",
        ["English"] = "English",
        ["Theme"] = "Tema",
        ["SystemTheme"] = "Sistem",
        ["LightTheme"] = "Aydınlık",
        ["DarkTheme"] = "Karanlık",
        ["AutoRefresh"] = "Otomatik yenile",
        ["RefreshInterval"] = "Yenileme aralığı",
        ["Refresh1s"] = "1 saniye",
        ["Refresh2s"] = "3 saniye",
        ["Refresh5s"] = "5 saniye",
        ["Tools"] = "Seçenekler",
        ["RefreshProcesses"] = "Processleri yenile",
        ["ApplyRules"] = "Kuralları uygula",
        ["StopAll"] = "Tüm limitleri durdur",
        ["CopyDiagnostic"] = "Tanı raporunu kopyala",
        ["Help"] = "Yardım",
        ["About"] = "IntLimiter hakkında",
        ["AboutTitle"] = "IntLimiter hakkında",
        ["AboutMessage"] = "IntLimiter MVP - WinDivert tabanlı bant genişliği izleme ve limit uygulama aracı.",
        ["Service"] = "Servis",
        ["Admin"] = "Yönetici",
        ["Mode"] = "Mod",
        ["Queue"] = "Kuyruk",
        ["Running"] = "Çalışıyor",
        ["Stopped"] = "Durduruldu",
        ["Unavailable"] = "Ulaşılamıyor",
        ["Elevated"] = "Yetkili",
        ["NotElevated"] = "Yetkisiz",
        ["Checking"] = "Kontrol ediliyor",
        ["RunSetupOnce"] = "Service kurulu değil. Bir kez setup çalıştır.",
        ["Packets"] = "paket",
        ["StartService"] = "Servisi başlat",
        ["StopService"] = "Servisi durdur",
        ["DeleteRule"] = "Kuralı sil",
        ["Processes"] = "Processler",
        ["ProcessesShort"] = "process",
        ["Search"] = "Ara",
        ["ActiveOnly"] = "Sadece aktifler",
        ["LoadingProcesses"] = "Processler yükleniyor...",
        ["RateUnit"] = "Hız birimi",
        ["Name"] = "Ad",
        ["Pid"] = "PID",
        ["Upload"] = "Upload",
        ["Download"] = "Download",
        ["Path"] = "Yol",
        ["SelectedProcessLimit"] = "Seçilen process limiti",
        ["DownloadLimit"] = "Download limiti",
        ["UploadLimit"] = "Upload limiti",
        ["Enabled"] = "Etkin",
        ["AddSelectedProcessLimit"] = "Seçili process limitini ekle",
        ["GlobalLimits"] = "Global limitler",
        ["GlobalDownloadLimit"] = "Global download limiti",
        ["GlobalUploadLimit"] = "Global upload limiti",
        ["AddGlobalLimits"] = "Global limitleri ekle",
        ["Rules"] = "Kurallar",
        ["Scope"] = "Kapsam",
        ["Direction"] = "Yön",
        ["Limit"] = "Limit",
        ["Process"] = "Process",
        ["DiagnosticsTest"] = "Tanılama / Test",
        ["RuntimeMode"] = "Çalışma modu",
        ["CapturedPackets"] = "Yakalanan paket",
        ["DelayedPackets"] = "Geciken paket",
        ["ReinjectedPackets"] = "Yeniden enjekte",
        ["DroppedPackets"] = "Düşen paket",
        ["QueueLength"] = "Kuyruk uzunluğu",
        ["MappingOkFail"] = "Eşleşme OK/hata",
        ["LastError"] = "Son hata",
        ["LastLogs"] = "Son 20 log satırı",
        ["RefreshDiagnostics"] = "Tanılamayı yenile",
        ["NoDiagnostics"] = "Henüz tanılama verisi yok.",
        ["ProcessRefreshFailed"] = "Process yenileme başarısız",
        ["RefreshDiagnosticsFailed"] = "Tanılama yenileme başarısız",
        ["WindowsServiceControlFailed"] = "Windows servis kontrolü başarısız",
        ["SelectProcessFirst"] = "Önce bir process seç.",
        ["DeleteRuleFailed"] = "Kural silme başarısız",
        ["StopAllFailed"] = "Tüm limitleri durdurma başarısız",
        ["ApplyRulesFailed"] = "Kuralları uygulama başarısız",
        ["GlobalDownloadRuleName"] = "Global download",
        ["GlobalUploadRuleName"] = "Global upload",
        ["IsRunning"] = "Çalışıyor",
        ["ActiveRules"] = "Aktif kurallar",
        ["Message"] = "Mesaj",
        ["Uptime"] = "Çalışma süresi",
        ["LogPath"] = "Log yolu",
        ["Monitoring"] = "İzleme",
        ["WinDivertActive"] = "WinDivert aktif",
        ["QosFallback"] = "QoS yedek modu",
        ["DisabledError"] = "Hata / devre dışı",
        ["StartingService"] = "Servis başlatılıyor"
    };

    private static readonly IReadOnlyDictionary<string, string> EnglishText = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["File"] = "File",
        ["OpenLogFolder"] = "Open log folder",
        ["Exit"] = "Exit",
        ["View"] = "View",
        ["Language"] = "Language",
        ["Turkish"] = "Turkish",
        ["English"] = "English",
        ["Theme"] = "Theme",
        ["SystemTheme"] = "System",
        ["LightTheme"] = "Light",
        ["DarkTheme"] = "Dark",
        ["AutoRefresh"] = "Auto refresh",
        ["RefreshInterval"] = "Refresh interval",
        ["Refresh1s"] = "1 second",
        ["Refresh2s"] = "3 seconds",
        ["Refresh5s"] = "5 seconds",
        ["Tools"] = "Options",
        ["RefreshProcesses"] = "Refresh processes",
        ["ApplyRules"] = "Apply rules",
        ["StopAll"] = "Stop all limits",
        ["CopyDiagnostic"] = "Copy diagnostic report",
        ["Help"] = "Help",
        ["About"] = "About IntLimiter",
        ["AboutTitle"] = "About IntLimiter",
        ["AboutMessage"] = "IntLimiter MVP - WinDivert based bandwidth monitoring and limiter.",
        ["Service"] = "Service",
        ["Admin"] = "Admin",
        ["Mode"] = "Mode",
        ["Queue"] = "Queue",
        ["Running"] = "Running",
        ["Stopped"] = "Stopped",
        ["Unavailable"] = "Unavailable",
        ["Elevated"] = "Elevated",
        ["NotElevated"] = "Not elevated",
        ["Checking"] = "Checking",
        ["RunSetupOnce"] = "Service is not installed. Run setup once.",
        ["Packets"] = "packets",
        ["StartService"] = "Start service",
        ["StopService"] = "Stop service",
        ["DeleteRule"] = "Delete rule",
        ["Processes"] = "Processes",
        ["ProcessesShort"] = "processes",
        ["Search"] = "Search",
        ["ActiveOnly"] = "Active only",
        ["LoadingProcesses"] = "Loading processes...",
        ["RateUnit"] = "Rate unit",
        ["Name"] = "Name",
        ["Pid"] = "PID",
        ["Upload"] = "Upload",
        ["Download"] = "Download",
        ["Path"] = "Path",
        ["SelectedProcessLimit"] = "Selected process limit",
        ["DownloadLimit"] = "Download limit",
        ["UploadLimit"] = "Upload limit",
        ["Enabled"] = "Enabled",
        ["AddSelectedProcessLimit"] = "Add selected process limit",
        ["GlobalLimits"] = "Global limits",
        ["GlobalDownloadLimit"] = "Global download limit",
        ["GlobalUploadLimit"] = "Global upload limit",
        ["AddGlobalLimits"] = "Add global limits",
        ["Rules"] = "Rules",
        ["Scope"] = "Scope",
        ["Direction"] = "Direction",
        ["Limit"] = "Limit",
        ["Process"] = "Process",
        ["DiagnosticsTest"] = "Diagnostics / Test",
        ["RuntimeMode"] = "Runtime mode",
        ["CapturedPackets"] = "Captured packets",
        ["DelayedPackets"] = "Delayed packets",
        ["ReinjectedPackets"] = "Reinjected packets",
        ["DroppedPackets"] = "Dropped packets",
        ["QueueLength"] = "Queue length",
        ["MappingOkFail"] = "Mapping OK/fail",
        ["LastError"] = "Last error",
        ["LastLogs"] = "Last 20 log lines",
        ["RefreshDiagnostics"] = "Refresh diagnostics",
        ["NoDiagnostics"] = "No diagnostics yet.",
        ["ProcessRefreshFailed"] = "Process refresh failed",
        ["RefreshDiagnosticsFailed"] = "Diagnostics refresh failed",
        ["WindowsServiceControlFailed"] = "Windows service control failed",
        ["SelectProcessFirst"] = "Select a process first.",
        ["DeleteRuleFailed"] = "Delete rule failed",
        ["StopAllFailed"] = "Stop all limits failed",
        ["ApplyRulesFailed"] = "Apply rules failed",
        ["GlobalDownloadRuleName"] = "Global download",
        ["GlobalUploadRuleName"] = "Global upload",
        ["IsRunning"] = "Running",
        ["ActiveRules"] = "Active rules",
        ["Message"] = "Message",
        ["Uptime"] = "Uptime",
        ["LogPath"] = "Log path",
        ["Monitoring"] = "Monitoring",
        ["WinDivertActive"] = "WinDivert active",
        ["QosFallback"] = "QoS fallback",
        ["DisabledError"] = "Disabled / error",
        ["StartingService"] = "Starting service"
    };

    private sealed class ClientSettings
    {
        public UiLanguage Language { get; set; } = UiLanguage.Turkish;
        public UiTheme Theme { get; set; } = UiTheme.System;
        public bool AutoRefresh { get; set; } = true;
        public int RefreshIntervalMs { get; set; } = 3000;
        public RateUnit RateUnit { get; set; } = RateUnit.KilobytesPerSecond;
    }

    private enum UiLanguage
    {
        Turkish,
        English
    }

    private enum UiTheme
    {
        System,
        Light,
        Dark
    }

    private enum ProcessSortMode
    {
        TotalTrafficDescending,
        NameAscending,
        UploadDescending,
        DownloadDescending
    }

    private enum RateUnit
    {
        BytesPerSecond,
        KilobytesPerSecond,
        MegabytesPerSecond,
        KilobitsPerSecond,
        MegabitsPerSecond
    }
}

