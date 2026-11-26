using CardPlatform.Messaging.Publishing;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Kafka;

/// <summary>
/// Creates the given topics when the host starts, before the background services (e.g. the outbox publisher) run.
/// Meant for local development, where nothing else creates them; topics that already exist are left as they are. A
/// failure is logged and startup goes on: publishing then fails, and is retried, until the topics exist.
/// </summary>
public sealed class KafkaTopicInitializer(
    string bootstrapServers,
    IReadOnlyCollection<string> topics,
    KafkaTopicOptions options,
    ILogger<KafkaTopicInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers })
            .Build();

        TopicSpecification[] specifications =
        [
            .. topics.Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = options.Partitions,
                ReplicationFactor = options.ReplicationFactor,
            }),
        ];

        try
        {
            await admin.CreateTopicsAsync(specifications, new CreateTopicsOptions { RequestTimeout = options.Timeout })
                .WaitAsync(options.Timeout, cancellationToken)
                .ConfigureAwait(false);

            foreach (TopicSpecification specification in specifications)
            {
                OutboxLog.TopicCreated(logger, specification.Name, specification.NumPartitions);
            }
        }
        catch (CreateTopicsException exception)
            when (exception.Results.All(static result =>
                result.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            foreach (CreateTopicReport report in exception.Results)
            {
                if (report.Error.Code == ErrorCode.TopicAlreadyExists)
                {
                    OutboxLog.TopicExists(logger, report.Topic);
                }
                else
                {
                    OutboxLog.TopicCreated(logger, report.Topic, options.Partitions);
                }
            }
        }
        catch (Exception exception) when (exception is KafkaException or TimeoutException)
        {
            OutboxLog.TopicsNotCreated(logger, exception, string.Join(", ", topics));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
