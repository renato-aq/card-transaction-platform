using CardPlatform.Fraud.Infrastructure;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Worker.Tests.Infrastructure;

/// <summary>
/// Builds the real Fraud Worker host (<see cref="FraudWorkerHost"/>) with the connection strings Aspire injects for
/// <c>.WithReference(fraudDb)</c> and <c>.WithReference(kafka)</c>, optionally consuming a test topic in a given group.
/// </summary>
internal static class WorkerHosts
{
    public static IHost Create(
        string databaseConnectionString,
        string kafkaBootstrapServers,
        string environment,
        string? topic = null,
        string? groupId = null,
        LogCapture? logs = null)
    {
        List<string> args =
        [
            $"--environment={environment}",
            $"--ConnectionStrings:{DependencyInjection.ConnectionName}={databaseConnectionString}",
            $"--ConnectionStrings:kafka={kafkaBootstrapServers}",
            "--Messaging:Consumer:RetryInitialDelay=00:00:00.200",
            "--Messaging:Consumer:RetryMaxDelay=00:00:01",
        ];

        if (topic is not null)
        {
            args.Add($"--Messaging:Consumer:Topics:0={topic}");
        }

        if (groupId is not null)
        {
            args.Add($"--Aspire:Confluent:Kafka:Consumer:Config:GroupId={groupId}");
        }

        HostApplicationBuilder builder = FraudWorkerHost.CreateBuilder([.. args]);
        if (logs is not null)
        {
            // A rule for this provider wins over the appsettings' Default: Information.
            builder.Logging.AddProvider(logs).AddFilter<LogCapture>(category: null, LogLevel.Debug);
        }

        return builder.Build();
    }
}
