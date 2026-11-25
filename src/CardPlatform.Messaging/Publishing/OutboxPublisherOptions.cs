using Microsoft.Extensions.Configuration;

namespace CardPlatform.Messaging.Publishing;

/// <summary>
/// Settings of the outbox publisher (configuration section <c>Messaging:Outbox</c>). See ADR 0010.
/// </summary>
public sealed class OutboxPublisherOptions
{
    public const string SectionName = "Messaging:Outbox";

    /// <summary>
    /// Default <see cref="LockKey"/>: an arbitrary constant, unique among the advisory locks of a service database.
    /// </summary>
    public const long DefaultLockKey = 0x4F7574626F78; // "Outbox" in ASCII

    /// <summary>
    /// Whether the publisher runs. When <see langword="false"/>, nothing Kafka-related is registered and the events stay
    /// in the outbox (e.g. integration tests that do not start a broker). Default <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many unpublished rows are read and published per cycle. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Wait between cycles when the outbox is empty or another instance is publishing. Default 1 s.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Wait after the first failed cycle; doubled after each further failure. Default 1 s.</summary>
    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound of the wait between failed cycles. Default 30 s.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Key of the PostgreSQL advisory lock taken by each cycle, so only one instance publishes at a time. Default
    /// <see cref="DefaultLockKey"/>.
    /// </summary>
    public long LockKey { get; set; } = DefaultLockKey;

    /// <summary>Reads <see cref="Enabled"/> from <paramref name="configuration"/> (default <see langword="true"/>).</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName).GetValue(nameof(Enabled), defaultValue: true);
    }

    internal bool IsValid()
    {
        return BatchSize > 0
            && PollingInterval > TimeSpan.Zero
            && RetryInitialDelay > TimeSpan.Zero
            && RetryMaxDelay >= RetryInitialDelay;
    }
}
