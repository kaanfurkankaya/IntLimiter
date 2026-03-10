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
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "IntLimiter - Ağ Yöneticisi",
            Visible = false
        };

        // Use the app's icon if available, otherwise use a default system icon
        try
        {
            _trayIcon.Icon = Drawing.Icon.ExtractAssociatedIcon(
                System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "")
                ?? Drawing.SystemIcons.Application;
        }
        catch
        {
            _trayIcon.Icon = Drawing.SystemIcons.Application;
        }

        // Context menu for tray
        var menu = new Forms.ContextMenuStrip();
        menu.BackColor = Drawing.Color.FromArgb(45, 45, 45);
        menu.ForeColor = Drawing.Color.White;
        menu.Renderer = new DarkMenuRenderer();

        var showItem = new Forms.ToolStripMenuItem("Göster");
        showItem.Click += (s, e) => ShowFromTray();

        var exitItem = new Forms.ToolStripMenuItem("Çıkış (Limitleri Temizle)");
        exitItem.Click += (s, e) => RealClose();

        menu.Items.Add(showItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (s, e) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon != null) _trayIcon.Visible = false;
    }

    private void RealClose()
    {
        _reallyClose = true;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        Close();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var monitor = new NetworkMonitorService(Dispatcher);
        _limiter = new BandwidthLimiterService();

        await _limiter.InitializeAsync();

        _viewModel = new MainViewModel(monitor, _limiter);
        DataContext = _viewModel;
        _viewModel.StartMonitoring();
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            Hide();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(2000, "IntLimiter",
                    "Uygulama sistem tepsisine küçültüldü", Forms.ToolTipIcon.Info);
            }
            return;
        }

        // Real close: clean up all limits
        if (_viewModel != null)
            await _viewModel.StopMonitoring();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }

    // Dark renderer for tray context menu
    private class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Drawing.Color.White;
            base.OnRenderItemText(e);
        }
    }

    private class DarkColorTable : Forms.ProfessionalColorTable
    {
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(60, 60, 60);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(70, 70, 70);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(70, 70, 70);
        public override Drawing.Color MenuItemSelectedGradientBegin => Drawing.Color.FromArgb(55, 55, 55);
        public override Drawing.Color MenuItemSelectedGradientEnd => Drawing.Color.FromArgb(55, 55, 55);
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(45, 45, 45);
        public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.FromArgb(45, 45, 45);
        public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.FromArgb(45, 45, 45);
        public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.FromArgb(45, 45, 45);
        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(70, 70, 70);
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(70, 70, 70);
    }
}