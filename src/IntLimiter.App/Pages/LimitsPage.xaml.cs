using Microsoft.UI.Xaml.Controls;
using IntLimiter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IntLimiter.App.Pages
{
    public sealed partial class LimitsPage : Page
    {
        public LimitsViewModel ViewModel { get; }

        public LimitsPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<LimitsViewModel>();
            this.InitializeComponent();
        }
    }
}
