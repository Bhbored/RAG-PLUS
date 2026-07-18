using System.Text.Json;
using StackExchange.Redis;

namespace Processor;

public class ScrapedDataProcessor : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ScrapedDataProcessor> _logger;

    public ScrapedDataProcessor(
        IConnectionMultiplexer redis,
        ILogger<ScrapedDataProcessor> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync("raw-data-ready", async (channel, message) =>
        {
            _logger.LogInformation("Received raw-data-ready for channel: {Channel}", channel);

            try
            {
                var notification = JsonSerializer.Deserialize<RawDataNotification>(message!);
                if (notification is not null)
                {
                    await ProcessAndIndexAsync(notification, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process raw-data-ready message");
            }
        });

        _logger.LogInformation("ScrapedDataProcessor started. Listening for raw-data-ready events...");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessAndIndexAsync(
        RawDataNotification notification,
        CancellationToken stoppingToken)
    {
        // TODO: Phase 3 - Fetch raw HTML, strip boilerplate, validate, version, index
        _logger.LogInformation(
            "Processing URL: {Url}, RawId: {Id}",
            notification.Url,
            notification.Id);

        await Task.CompletedTask;
    }
}

public record RawDataNotification(string Url, string Id);
