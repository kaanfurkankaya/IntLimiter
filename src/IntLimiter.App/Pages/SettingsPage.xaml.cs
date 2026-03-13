using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IntLimiter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IntLimiter.App.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
        }

        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    }
}
