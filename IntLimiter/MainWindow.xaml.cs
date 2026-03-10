using IntLimiter.Services;
using IntLimiter.ViewModels;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace IntLimiter;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private BandwidthLimiterService? _limiter;
    private Forms.NotifyIcon? _trayIcon;
    private bool _reallyClose;

    public MainWindow()
    {
        InitializeComponent();
        // Tray icon setup deferred to Loaded event to avoid constructor crash
    }

    private void SetupTrayIcon()
    {
        try
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "IntLimiter - Ağ Yöneticisi",
                Icon = Drawing.SystemIcons.Application,
                Visible = false
            };

            // Try to use app icon
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var appIcon = Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (appIcon != null) _trayIcon.Icon = appIcon;
                }
            }
            catch { /* fallback to SystemIcons.Application */ }

            var menu = new Forms.ContextMenuStrip();
            menu.BackColor = Drawing.Color.FromArgb(45, 45, 45);
            menu.ForeColor = Drawing.Color.White;

            var showItem = new Forms.ToolStripMenuItem("Göster");
            showItem.Click += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    if (_trayIcon != null) _trayIcon.Visible = false;
                });
            };

            var exitItem = new Forms.ToolStripMenuItem("Çıkış (Limitleri Temizle)");
            exitItem.Click += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _reallyClose = true;
                    DisposeTray();
                    Close();
                });
            };

            menu.Items.Add(showItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    if (_trayIcon != null) _trayIcon.Visible = false;
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray setup failed: {ex.Message}");
        }
    }

    private void DisposeTray()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Setup tray AFTER window is fully loaded
        SetupTrayIcon();

        var monitor = new NetworkMonitorService(Dispatcher);
        _limiter = new BandwidthLimiterService();
        _viewModel = new MainViewModel(monitor, _limiter);
        DataContext = _viewModel;
        _viewModel.StartMonitoring();

        // QoS init in background
        Task.Run(async () =>
        {
            try { await _limiter.InitializeAsync(); }
            catch { }
        });
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true;
            Hide();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(2000, "IntLimiter",
                    "Sistem tepsisine küçültüldü", Forms.ToolTipIcon.Info);
            }
            return;
        }

        // Real close: clean up everything
        if (_viewModel != null)
            await _viewModel.StopMonitoring();

        DisposeTray();
    }
}