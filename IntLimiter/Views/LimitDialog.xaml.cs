using System.Windows;

namespace IntLimiter.Views;

public partial class LimitDialog : Window
{
    /// <summary>
    /// Returns the limit in bits per second.
    /// </summary>
    public long DownloadLimitBitsPerSecond { get; private set; }
    public long UploadLimitBitsPerSecond { get; private set; }

    public LimitDialog(string processName, long currentDownloadLimit, long currentUploadLimit, bool isGlobal = false)
    {
        InitializeComponent();

        if (isGlobal)
            ProcessNameText.Text = "🌐 Tüm PC internet trafiğini sınırla";
        else
            ProcessNameText.Text = $"İşlem: {processName}";

        // Pre-fill current limits (stored as bits/s)
        if (currentDownloadLimit > 0)
        {
            if (currentDownloadLimit >= 1_000_000)
            {
                DownloadLimitBox.Text = (currentDownloadLimit / 1_000_000.0).ToString("F1");
                DownloadUnitCombo.SelectedIndex = 1; // Mbit/s
            }
            else
            {
                DownloadLimitBox.Text = (currentDownloadLimit / 1_000.0).ToString("F0");
                DownloadUnitCombo.SelectedIndex = 0; // Kbit/s
            }
        }

        if (currentUploadLimit > 0)
        {
            if (currentUploadLimit >= 1_000_000)
            {
                UploadLimitBox.Text = (currentUploadLimit / 1_000_000.0).ToString("F1");
                UploadUnitCombo.SelectedIndex = 1;
            }
            else
            {
                UploadLimitBox.Text = (currentUploadLimit / 1_000.0).ToString("F0");
                UploadUnitCombo.SelectedIndex = 0;
            }
        }
    }

    private long ConvertToBlitsPerSecond(double value, int unitIndex)
    {
        // 0=Kbit/s  1=Mbit/s  2=KB/s  3=MB/s
        return unitIndex switch
        {
            0 => (long)(value * 1_000),           // Kbit/s → bit/s
            1 => (long)(value * 1_000_000),       // Mbit/s → bit/s
            2 => (long)(value * 8 * 1_024),       // KB/s → bit/s
            3 => (long)(value * 8 * 1_048_576),   // MB/s → bit/s
            _ => 0
        };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(DownloadLimitBox.Text, out var dlVal) && dlVal > 0)
            DownloadLimitBitsPerSecond = ConvertToBlitsPerSecond(dlVal, DownloadUnitCombo.SelectedIndex);
        else
            DownloadLimitBitsPerSecond = 0;

        if (double.TryParse(UploadLimitBox.Text, out var ulVal) && ulVal > 0)
            UploadLimitBitsPerSecond = ConvertToBlitsPerSecond(ulVal, UploadUnitCombo.SelectedIndex);
        else
            UploadLimitBitsPerSecond = 0;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
