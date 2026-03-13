using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace IntLimiter.App;

public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();

        XamlCheckProcessRequirements();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
