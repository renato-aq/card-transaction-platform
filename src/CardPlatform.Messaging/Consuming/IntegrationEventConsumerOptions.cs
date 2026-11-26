using Microsoft.Extensions.Configuration;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Settings of the integration event consumer (configuration section <c>Messaging:Consumer</c>). See ADR 0011. The
/// consumer group and the other Kafka client settings are under <c>Aspire:Confluent:Kafka:Consumer:Config</c>.
/// </summary>
public sealed class IntegrationEventConsumerOptions
{
    public const string SectionName = "Messaging:Consumer";

    /// <summary>
    /// Whether the consumer runs. When <see langword="false"/>, nothing Kafka-related is registered and no event is
    /// consumed (e.g. integration tests that do not start a broker). Default <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The topics to consume. When none is configured, the topics given by the service when it registered the consumer.
    /// </summary>
    public IList<string> Topics { get; } = [];

    /// <summary>
    /// How many times a message that fails with a temporary error (e.g. the database is down) is processed before it is
    /// sent to its dead-letter topic (<c>&lt;topic&gt;.dlt</c>), the first attempt included. A permanent error (a malformed
    /// message, <see cref="IntegrationEventRejectedException"/>) sends it there after the first. Default 5.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Wait before the first retry of a message that failed; doubled after each further failure. Default 1 s.</summary>
    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound of the wait between retries. Default 30 s.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Reads <see cref="Enabled"/> from <paramref name="configuration"/> (default <see langword="true"/>).</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName).GetValue(nameof(Enabled), defaultValue: true);
    }

    internal bool IsValid()
    {
        return Topics.Count > 0
            && Topics.All(static topic => !string.IsNullOrWhiteSpace(topic))
            && MaxAttempts >= 1
            && RetryInitialDelay > TimeSpan.Zero
            && RetryMaxDelay >= RetryInitialDelay;
    }
}
