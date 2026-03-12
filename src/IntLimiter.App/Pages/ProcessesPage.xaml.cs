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
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<ProcessesViewModel>();
        }
    }
}
