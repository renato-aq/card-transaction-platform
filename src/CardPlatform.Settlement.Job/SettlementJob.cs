namespace CardPlatform.Settlement.Job;

// Will generate the daily settlement file from the ledger in batches, then exit.
public sealed partial class SettlementJob(
    ILogger<SettlementJob> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogFinished();

        lifetime.StopApplication();
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settlement job finished")]
    private partial void LogFinished();
}
