using OlympiadGate.Core;
using OlympiadGate.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "OlympiadGate");
builder.Services.AddSingleton<FileLog>();
builder.Services.AddSingleton(_ => new GateCatalog(AppPaths.DatabasePath));
builder.Services.AddSingleton<GateEndpoints>();
builder.Services.AddSingleton<ProcessLauncher>();
builder.Services.AddSingleton<SessionGuard>();
builder.Services.AddHostedService<GateWorker>();

var host = builder.Build();
host.Run();
