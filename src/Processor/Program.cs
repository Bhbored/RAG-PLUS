using Processor;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ProcessorOptions>(
    builder.Configuration.GetSection("Processor"));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

builder.Services.AddHostedService<ScrapedDataProcessor>();

var host = builder.Build();
await host.RunAsync();
