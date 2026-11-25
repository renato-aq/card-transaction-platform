namespace CardPlatform.Authorization.Infrastructure.Ledger;

/// <summary>
/// Settings of the Ledger HTTP client, bound from the <c>Ledger</c> configuration section. The defaults are tuned for
/// authorization calls: a short total budget (a POS cannot wait long) and a couple of quick retries for safe calls.
/// </summary>
public sealed class LedgerClientOptions
{
    public const string SectionName = "Ledger";

    /// <summary>Base address; <c>https+http://ledger-api</c> is resolved by Aspire service discovery.</summary>
    public Uri BaseAddress { get; set; } = new("https+http://ledger-api");

    /// <summary>Timeout of a single attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Timeout of the whole call, retries included.</summary>
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Retries after the first attempt (only for calls that are safe to repeat).</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Base delay of the exponential, jittered backoff between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}
