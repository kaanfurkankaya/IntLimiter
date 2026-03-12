using IntLimiter.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args);
builder.UseWindowsService(options =>
{
    options.ServiceName = "IntLimiter Service";
});

builder.Services.AddHostedService<TrafficControllerWorker>();

var host = builder.Build();
host.Run();
