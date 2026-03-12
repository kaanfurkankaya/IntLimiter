using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IntLimiter.App.Pages;

namespace IntLimiter.App
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
            
            NavView.SelectedItem = NavView.MenuItems[0];
            Navigate("DashboardPage");
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                Navigate("SettingsPage");
                return;
            }

            if (args.SelectedItem is NavigationViewItem navItem)
            {
                Navigate(navItem.Tag?.ToString());
            }
        }
        
        private void Navigate(string? pageTag)
        {
            Type? pageType = pageTag switch
            {
                "DashboardPage" => typeof(DashboardPage),
                "ProcessesPage" => typeof(ProcessesPage),
                "LimitsPage" => typeof(LimitsPage),
                "SettingsPage" => typeof(SettingsPage),
                _ => null
            };

            if (pageType != null)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
