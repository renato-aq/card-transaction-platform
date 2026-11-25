namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Settings of the integration event consumer (configuration section <c>Messaging:Consumer</c>). See ADR 0011. The
/// consumer group and the other Kafka client settings are under <c>Aspire:Confluent:Kafka:Consumer:Config</c>.
/// </summary>
public sealed class IntegrationEventConsumerOptions
{
    public const string SectionName = "Messaging:Consumer";

    /// <summary>
    /// The topics to consume. When none is configured, the topics given by the service when it registered the consumer.
    /// </summary>
    public IList<string> Topics { get; } = [];

    /// <summary>Wait before the first retry of a message that failed; doubled after each further failure. Default 1 s.</summary>
    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound of the wait between retries. Default 30 s.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsValid()
    {
        return Topics.Count > 0
            && Topics.All(static topic => !string.IsNullOrWhiteSpace(topic))
            && RetryInitialDelay > TimeSpan.Zero
            && RetryMaxDelay >= RetryInitialDelay;
    }
}
