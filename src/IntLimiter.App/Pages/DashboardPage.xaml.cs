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
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<DashboardViewModel>();
        }
    }
}
