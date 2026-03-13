using Microsoft.UI.Xaml.Controls;
using IntLimiter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IntLimiter.App.Pages
{
    public sealed partial class ProcessesPage : Page
    {
        public ProcessesViewModel ViewModel { get; }

        public ProcessesPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<ProcessesViewModel>();
            this.InitializeComponent();
            ViewModel.UiDispatch = action => DispatcherQueue.TryEnqueue(() => action());
            ViewModel.RefreshFromMonitor();
        }
    }
}
