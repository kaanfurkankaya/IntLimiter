using System;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using IntLimiter.Core.Contracts;
using IntLimiter.Monitoring;
using IntLimiter.UI.ViewModels;

namespace IntLimiter.App
{
    public partial class App : Application
    {
        private Window? m_window;
        
        public IServiceProvider Services { get; }

        public new static App Current => (App)Application.Current;

        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Core & Monitoring
            services.AddSingleton<ITrafficMonitor, TrafficMonitor>();

            // Infrastructure & RateLimiting
            services.AddSingleton<IntLimiter.Infrastructure.Stores.RuleStore>();
            services.AddSingleton<IRuleEngine>(provider => 
            {
                var store = provider.GetRequiredService<IntLimiter.Infrastructure.Stores.RuleStore>();
                var initialRules = store.Load();
                return new IntLimiter.RateLimiting.RuleEngine(initialRules, rules => store.Save(rules));
            });
            services.AddSingleton<IntLimiter.Updater.IUpdateEngine, IntLimiter.Updater.UpdateEngine>();

            // ViewModels
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<ProcessesViewModel>();
            services.AddTransient<LimitsViewModel>();
            services.AddTransient<SettingsViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var monitor = Services.GetRequiredService<ITrafficMonitor>();
            try 
            {
                monitor.Start();
            }
            catch (UnauthorizedAccessException)
            {
                // To be handled gracefully in UI
            }
            catch (Exception ex)
            {
                var crashPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "STARTUP_CRASH_REPORT.txt");
                System.IO.File.WriteAllText(crashPath, $"CRITICAL STARTUP CRASH:\n{ex.GetType().Name}\n{ex.Message}\n\nSTACK TRACE:\n{ex.StackTrace}\n\nINNER EXCEPTION:\n{ex.InnerException?.Message}\n{ex.InnerException?.StackTrace}");
            }

            m_window = new MainWindow();
            m_window.Activate();
        }
    }
}
