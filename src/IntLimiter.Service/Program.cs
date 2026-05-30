using IntLimiter.Core.Contracts;
using IntLimiter.Core.Logging;
using IntLimiter.Core.Monitoring;
using IntLimiter.Core.Persistence;
using IntLimiter.DriverBridge;
using IntLimiter.DriverBridge.Qos;
using IntLimiter.DriverBridge.WinDivert;
using IntLimiter.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "IntLimiter.Service";
});

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Services.AddSingleton<IAppLog, JsonLineAppLog>();
builder.Services.AddSingleton<IRuleStore, JsonRuleStore>();
builder.Services.AddSingleton<IProcessNetworkMonitor, ProcessNetworkMonitor>();
builder.Services.AddSingleton<WinDivertTrafficLimiter>();
builder.Services.AddSingleton<QosPolicyLimiter>();
builder.Services.AddSingleton<ITrafficLimiter, HybridTrafficLimiter>();
builder.Services.AddSingleton<LimiterCoordinator>();
builder.Services.AddHostedService<NamedPipeIpcServer>();

var host = builder.Build();
host.Run();
