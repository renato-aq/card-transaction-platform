namespace CardPlatform.Contracts;

/// <summary>Names of the Kafka topics the services exchange events on. Every topic is keyed by card id.</summary>
public static class Topics
{
    /// <summary>Decisions of the Authorization (approved, declined, captured, voided), consumed by the Fraud Worker.</summary>
    public const string Authorizations = "authorizations";

    /// <summary>Fraud suspicions raised by the Fraud Worker, consumed by the Authorization.</summary>
    public const string FraudSignals = "fraud-signals";

    private const string DeadLetterSuffix = ".dlt";

    /// <summary>Name of the dead-letter topic of <paramref name="topic"/> (<c>&lt;topic&gt;.dlt</c>).</summary>
    public static string DeadLetterOf(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        return topic + DeadLetterSuffix;
    }
}
