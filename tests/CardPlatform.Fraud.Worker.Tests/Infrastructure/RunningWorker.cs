using System.Globalization;

using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Infrastructure.Persistence;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Fraud.Worker.Tests.Infrastructure;

/// <summary>A started worker host (Development, so it migrates its database), stopped when disposed.</summary>
internal sealed class RunningWorker : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IHost _host;

    private RunningWorker(IHost host, LogCapture logs)
    {
        _host = host;
        Logs = logs;
    }

    public LogCapture Logs { get; }

    public static async Task<RunningWorker> StartAsync(
        PostgreSqlFixture postgres,
        KafkaFixture kafka,
        string topic,
        string groupId,
        IEnumerable<string>? settings = null)
    {
        return await StartAsync(await postgres.CreateDatabaseAsync(), kafka, topic, groupId, settings);
    }

    public static async Task<RunningWorker> StartAsync(
        string database,
        KafkaFixture kafka,
        string topic,
        string groupId,
        IEnumerable<string>? settings = null)
    {
        LogCapture logs = new();
        IHost host = WorkerHosts.Create(database, kafka.BootstrapServers, Environments.Development, topic, groupId, logs, settings);
        await FraudWorkerHost.InitializeAsync(host);
        await host.StartAsync();
        return new RunningWorker(host, logs);
    }

    /// <summary>Waits until <paramref name="condition"/> holds, failing the test after <see cref="Timeout"/>.</summary>
    public static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The condition was not met within {Timeout}.");
            }

            await Task.Delay(200);
        }
    }

    public async Task<CardHistory> HistoryAsync(Guid cardId)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICardPurchaseRepository>().GetHistoryAsync(cardId);
    }

    public async Task<CardHistory> WaitForHistoryAsync(Guid cardId, Func<CardHistory, bool> condition)
    {
        CardHistory history = await HistoryAsync(cardId);
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition(history))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The history of card {cardId} did not reach the expected state within {Timeout} ({history.Purchases.Count} purchase(s))."));
            }

            await Task.Delay(200);
            history = await HistoryAsync(cardId);
        }

        return history;
    }

    public async Task<List<ProcessedMessage>> ProcessedMessagesAsync()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FraudDbContext>().ProcessedMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>The outbox rows of the Fraud Worker, in the order they were written.</summary>
    public async Task<List<OutboxMessage>> OutboxMessagesAsync()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FraudDbContext>().OutboxMessages
            .AsNoTracking()
            .OrderBy(static message => message.Id)
            .ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
