using System.Windows;
using System.Windows.Threading;

namespace IntLimiter;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Show error instead of silently swallowing
        System.Windows.MessageBox.Show(
            $"Hata: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "IntLimiter Hata",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
