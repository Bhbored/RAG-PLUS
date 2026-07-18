using Microsoft.EntityFrameworkCore;
using Processor;
using Processor.Data;
using Processor.Services;
using Processor.Validation;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ProcessorOptions>(
    builder.Configuration.GetSection("Processor"));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Port=5432;Database=ragplus;Username=postgres;Password=123456",
        npgsqlOptions => npgsqlOptions.UseVector()));

builder.Services.AddScoped<RawDataRepository>();
builder.Services.AddScoped<CleanDataRepository>();
builder.Services.AddScoped<HtmlCleaner>();
builder.Services.AddScoped<ScrapedContentValidator>();
builder.Services.AddScoped<ChunkingService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddHttpClient<EmbeddingService>();

builder.Services.AddHostedService<ScrapedDataProcessor>();

var host = builder.Build();

// Auto-migrate on startup
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    Console.WriteLine("[DB] EF Core schema ensured (cleaned_data table ready)");
}

await host.RunAsync();
