using IntLimiter.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "IntLimiter Service";
});

builder.Services.AddHostedService<TrafficControllerWorker>();

var host = builder.Build();
host.Run();
