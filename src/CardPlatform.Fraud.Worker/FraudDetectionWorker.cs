namespace CardPlatform.Fraud.Worker;

// Will consume transaction events from Kafka and flag suspicious patterns.
public sealed partial class FraudDetectionWorker(ILogger<FraudDetectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted();

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fraud detection worker started")]
    private partial void LogStarted();
}
