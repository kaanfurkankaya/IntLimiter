using Microsoft.UI.Xaml.Controls;
using IntLimiter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IntLimiter.App.Pages
{
    public sealed partial class DashboardPage : Page
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<DashboardViewModel>();
            this.InitializeComponent();
            ViewModel.UiDispatch = action => DispatcherQueue.TryEnqueue(() => action());
            ViewModel.RefreshFromMonitor();
        }
    }
}
